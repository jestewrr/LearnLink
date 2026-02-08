using System.Diagnostics;
using LearnLink.Models;
using Microsoft.AspNetCore.Mvc;

namespace LearnLink.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Login");
        }

        // Authentication
        public IActionResult Login()
        {
            return View();
        }

        public IActionResult Register()
        {
            return View();
        }

        // Dashboard
        public IActionResult Dashboard()
        {
            return View();
        }

        // Knowledge Repository Module
        public IActionResult Repository()
        {
            return View();
        }

        public IActionResult Search()
        {
            return View();
        }

        // Content Management
        public IActionResult Upload()
        {
            return View();
        }

        public IActionResult MyUploads()
        {
            return View();
        }

        // Policy and Procedure Management Module
        public IActionResult Policies()
        {
            return View();
        }

        // Lesson Learned Management Module
        public IActionResult LessonsLearned()
        {
            return View();
        }

        public IActionResult ReadingHistory()
        {
            return View();
        }

        // Best Practices Module
        public IActionResult BestPractices()
        {
            return View();
        }

        // Knowledge Sharing Portal Module
        public IActionResult KnowledgePortal()
        {
            return View();
        }

        public IActionResult Discussions()
        {
            return View();
        }

        // Administration
        public IActionResult Users()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }

        public IActionResult Reports()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
