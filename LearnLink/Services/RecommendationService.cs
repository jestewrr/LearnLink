using LearnLink.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LearnLink.Services
{
    /// <summary>
    /// KNN-based collaborative filtering recommendation engine.
    /// Uses item-based KNN with cosine similarity on user–resource interaction vectors.
    /// 
    /// Interaction signals and weights:
    ///   View=1, Download=2, Comment=3, Bookmark/Save=3, Like=4, Complete=4, Rating=5
    /// 
    /// Two main operations:
    ///   1. GetSimilarResources – finds resources with similar user-interaction patterns (item-based KNN)
    ///   2. GetPersonalizedRecommendations – finds unread resources similar to what a user has engaged with
    /// </summary>
    public interface IRecommendationService
    {
        /// <summary>
        /// Get resources similar to the given resource using item-based KNN (cosine similarity on interaction vectors).
        /// </summary>
        Task<List<int>> GetSimilarResourcesAsync(int resourceId, int topN = 6, int? schoolId = null);

        /// <summary>
        /// Get personalized resource recommendations for a user based on their interaction history.
        /// Returns resource IDs the user has NOT yet interacted with, ranked by predicted relevance.
        /// </summary>
        Task<List<int>> GetPersonalizedRecommendationsAsync(string userId, int topN = 8, int? schoolId = null);
    }

    public class KnnRecommendationService : IRecommendationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        // Interaction weights — higher = stronger engagement signal
        private const double W_VIEW = 1.0;
        private const double W_DOWNLOAD = 2.0;
        private const double W_COMMENT = 3.0;
        private const double W_BOOKMARK = 3.0;
        private const double W_LIKE = 4.0;
        private const double W_COMPLETE = 4.0;
        private const double W_RATING = 5.0;

        // KNN parameter
        private const int K_NEIGHBORS = 10;

        public KnnRecommendationService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════════════════════════

        public async Task<List<int>> GetSimilarResourcesAsync(int resourceId, int topN = 6, int? schoolId = null)
        {
            var matrix = await GetInteractionMatrixAsync(schoolId);
            if (!matrix.ResourceVectors.ContainsKey(resourceId))
                return new List<int>();

            var targetVector = matrix.ResourceVectors[resourceId];
            var similarities = new List<(int ResourceId, double Score)>();

            foreach (var kvp in matrix.ResourceVectors)
            {
                if (kvp.Key == resourceId) continue;
                double sim = CosineSimilarity(targetVector, kvp.Value, matrix.UserIndex);
                if (sim > 0.0)
                    similarities.Add((kvp.Key, sim));
            }

            return similarities
                .OrderByDescending(s => s.Score)
                .Take(topN)
                .Select(s => s.ResourceId)
                .ToList();
        }

        public async Task<List<int>> GetPersonalizedRecommendationsAsync(string userId, int topN = 8, int? schoolId = null)
        {
            var matrix = await GetInteractionMatrixAsync(schoolId);

            // Get resources the user has interacted with and their scores
            var userInteractions = new Dictionary<int, double>(); // resourceId → score
            foreach (var kvp in matrix.ResourceVectors)
            {
                int userIdx;
                if (matrix.UserIndex.TryGetValue(userId, out userIdx) && kvp.Value.ContainsKey(userIdx))
                {
                    userInteractions[kvp.Key] = kvp.Value[userIdx];
                }
            }

            if (!userInteractions.Any())
            {
                // Cold start: return popular resources
                return await GetPopularResourceIdsAsync(topN, schoolId);
            }

            var interactedSet = userInteractions.Keys.ToHashSet();

            // For each candidate resource (not yet interacted), compute predicted score
            // using weighted average of similarities to the user's interacted resources
            var candidates = new List<(int ResourceId, double PredictedScore)>();

            foreach (var kvp in matrix.ResourceVectors)
            {
                if (interactedSet.Contains(kvp.Key)) continue;

                double numerator = 0;
                double denominator = 0;

                foreach (var interacted in userInteractions)
                {
                    if (!matrix.ResourceVectors.ContainsKey(interacted.Key)) continue;

                    double sim = CosineSimilarity(kvp.Value, matrix.ResourceVectors[interacted.Key], matrix.UserIndex);
                    if (sim > 0)
                    {
                        numerator += sim * interacted.Value;
                        denominator += Math.Abs(sim);
                    }
                }

                if (denominator > 0)
                {
                    candidates.Add((kvp.Key, numerator / denominator));
                }
            }

            var results = candidates
                .OrderByDescending(c => c.PredictedScore)
                .Take(topN)
                .Select(c => c.ResourceId)
                .ToList();

            // If not enough candidates, pad with popular resources
            if (results.Count < topN)
            {
                var popular = await GetPopularResourceIdsAsync(topN - results.Count, schoolId);
                var existing = results.ToHashSet();
                existing.UnionWith(interactedSet);
                results.AddRange(popular.Where(id => !existing.Contains(id)));
            }

            return results.Take(topN).ToList();
        }

        // ════════════════════════════════════════════════════════════════
        //  INTERACTION MATRIX CONSTRUCTION
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a sparse interaction matrix: Resource → { UserIndex → Score }.
        /// Cached for 15 minutes to avoid expensive DB queries on every page load.
        /// </summary>
        private async Task<InteractionMatrix> GetInteractionMatrixAsync(int? schoolId)
        {
            string cacheKey = $"knn_matrix_{schoolId ?? 0}";
            if (_cache.TryGetValue(cacheKey, out InteractionMatrix? cached) && cached != null)
                return cached;

            var matrix = await BuildInteractionMatrixAsync(schoolId);
            _cache.Set(cacheKey, matrix, CacheDuration);
            return matrix;
        }

        private async Task<InteractionMatrix> BuildInteractionMatrixAsync(int? schoolId)
        {
            // 1. Get all published resource IDs
            var resourceIds = await _context.Resources
                .Where(r => r.Status == "Published")
                .Select(r => r.ResourceId)
                .ToListAsync();
            var resourceSet = resourceIds.ToHashSet();

            // 2. Build user index (string userId → int index)
            var userIndex = new Dictionary<string, int>();
            int nextIdx = 0;

            // 3. Sparse vectors: resourceId → { userIdx → score }
            var resourceVectors = new Dictionary<int, Dictionary<int, double>>();
            foreach (var rid in resourceIds)
                resourceVectors[rid] = new Dictionary<int, double>();

            int GetOrAddUser(string uid)
            {
                if (!userIndex.TryGetValue(uid, out int idx))
                {
                    idx = nextIdx++;
                    userIndex[uid] = idx;
                }
                return idx;
            }

            void AddScore(int resourceId, string userId, double weight)
            {
                if (!resourceSet.Contains(resourceId)) return;
                int uidx = GetOrAddUser(userId);
                if (!resourceVectors.ContainsKey(resourceId)) return;
                resourceVectors[resourceId].TryGetValue(uidx, out double existing);
                resourceVectors[resourceId][uidx] = existing + weight;
            }

            // ── Gather interaction signals ──

            // Views (from activity logs)
            var views = await _context.UserActivityLogs
                .Where(a => a.ActivityType == "View" && a.ResourceId != null)
                .Select(a => new { a.UserId, ResourceId = a.ResourceId!.Value })
                .ToListAsync();
            foreach (var v in views)
                AddScore(v.ResourceId, v.UserId, W_VIEW);

            // Downloads
            var downloads = await _context.UserActivityLogs
                .Where(a => a.ActivityType == "Download" && a.ResourceId != null)
                .Select(a => new { a.UserId, ResourceId = a.ResourceId!.Value })
                .ToListAsync();
            foreach (var d in downloads)
                AddScore(d.ResourceId, d.UserId, W_DOWNLOAD);

            // Comments
            var comments = await _context.ResourceComments
                .Where(c => c.ParentCommentId == null) // only top-level
                .Select(c => new { c.UserId, c.ResourceId })
                .ToListAsync();
            foreach (var c in comments)
                AddScore(c.ResourceId, c.UserId, W_COMMENT);

            // Bookmarks / Saves
            var bookmarks = await _context.ReadingHistories
                .Where(h => h.IsBookmarked)
                .Select(h => new { h.UserId, h.ResourceId })
                .ToListAsync();
            foreach (var b in bookmarks)
                AddScore(b.ResourceId, b.UserId, W_BOOKMARK);

            // Completions
            var completions = await _context.ReadingHistories
                .Where(h => h.ProgressStatus == "Completed")
                .Select(h => new { h.UserId, h.ResourceId })
                .ToListAsync();
            foreach (var c in completions)
                AddScore(c.ResourceId, c.UserId, W_COMPLETE);

            // Likes (TargetType == "Resource")
            var likes = await _context.Likes
                .Where(l => l.TargetType == "Resource")
                .Select(l => new { l.UserId, l.TargetId })
                .ToListAsync();
            foreach (var l in likes)
                AddScore(l.TargetId, l.UserId, W_LIKE);

            // Ratings (TargetType == "ResourceRating")
            var ratings = await _context.Likes
                .Where(l => l.TargetType == "ResourceRating")
                .Select(l => new { l.UserId, l.TargetId })
                .ToListAsync();
            foreach (var r in ratings)
                AddScore(r.TargetId, r.UserId, W_RATING);

            return new InteractionMatrix
            {
                UserIndex = userIndex,
                ResourceVectors = resourceVectors
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  MATH UTILITIES
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cosine similarity between two sparse vectors (Dictionary&lt;int, double&gt;).
        /// </summary>
        private static double CosineSimilarity(
            Dictionary<int, double> vecA,
            Dictionary<int, double> vecB,
            Dictionary<string, int> userIndex)
        {
            if (vecA.Count == 0 || vecB.Count == 0) return 0;

            double dotProduct = 0;
            double normA = 0;
            double normB = 0;

            // Iterate over the smaller vector for efficiency
            var smaller = vecA.Count <= vecB.Count ? vecA : vecB;
            var larger = vecA.Count <= vecB.Count ? vecB : vecA;

            foreach (var kvp in smaller)
            {
                normA += kvp.Value * kvp.Value;
                if (larger.TryGetValue(kvp.Key, out double otherVal))
                {
                    dotProduct += kvp.Value * otherVal;
                }
            }

            // Need to compute norm for the larger vector fully
            foreach (var kvp in larger)
            {
                normB += kvp.Value * kvp.Value;
            }

            // If we swapped, normA is actually for smaller which could be either A or B
            // We need both full norms
            if (vecA.Count > vecB.Count)
            {
                // smaller was vecB, larger was vecA
                // normA has vecB's norm, normB has vecA's norm → swap
                (normA, normB) = (normB, normA);
            }

            double denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denominator == 0 ? 0 : dotProduct / denominator;
        }

        /// <summary>
        /// Fallback for cold-start users: return most popular published resources.
        /// </summary>
        private async Task<List<int>> GetPopularResourceIdsAsync(int topN, int? schoolId)
        {
            return await _context.Resources
                .Where(r => r.Status == "Published")
                .OrderByDescending(r => r.ViewCount + r.DownloadCount + (r.Rating * r.RatingCount))
                .Take(topN)
                .Select(r => r.ResourceId)
                .ToListAsync();
        }

        // ════════════════════════════════════════════════════════════════
        //  DATA STRUCTURES
        // ════════════════════════════════════════════════════════════════

        private class InteractionMatrix
        {
            /// <summary>Maps UserId (string) → integer index for sparse vectors.</summary>
            public Dictionary<string, int> UserIndex { get; set; } = new();

            /// <summary>Maps ResourceId → sparse vector { UserIndex → interaction score }.</summary>
            public Dictionary<int, Dictionary<int, double>> ResourceVectors { get; set; } = new();
        }
    }
}
