// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(function() {
    const modalElement = document.getElementById('llTableDetailModal');
    if (!modalElement || typeof bootstrap === 'undefined') {
        return;
    }

    const modalTitle = modalElement.querySelector('.modal-title');
    const modalBody = modalElement.querySelector('.modal-body');
    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);

    // Handle mobile card item clicks → show detail modal
    document.querySelectorAll('.ll-mobile-card-item').forEach(card => {
        card.addEventListener('click', function() {
            const data = this.dataset;
            const pageTitle = document.querySelector('.ll-page-title');
            modalTitle.textContent = pageTitle ? `${pageTitle.textContent} Details` : 'Details';

            const fields = [];
            if (data.resTitle) {
                let resourceVal = data.resTitle;
                if (data.resFormat || data.resSize) {
                    resourceVal += ` • ${[data.resFormat, data.resSize].filter(Boolean).join(' • ')}`;
                }
                fields.push({ label: 'Resource', value: resourceVal });
            }
            if (data.resSubject) fields.push({ label: 'Subject', value: data.resSubject });
            if (data.resGrade) fields.push({ label: 'Grade Level', value: data.resGrade });
            if (data.resType) fields.push({ label: 'Type', value: data.resType });
            if (data.resUploader) fields.push({ label: 'Contributor', value: data.resUploader });
            if (data.resDate) fields.push({ label: 'Date', value: data.resDate });
            if (data.resStatus) fields.push({ label: 'Status', value: data.resStatus });
            if (data.resViews) fields.push({ label: 'Views', value: data.resViews });
            if (data.resDownloads) fields.push({ label: 'Downloads', value: data.resDownloads });
            if (data.resRating) fields.push({ label: 'Rating', value: data.resRating });

            let actionsHtml = '';
            if (data.actions) {
                const actions = data.actions.split('|').map(a => a.trim()).filter(Boolean);
                if (actions.length) {
                    actionsHtml = '<div class="mt-3 d-grid gap-2">' +
                        actions.map(action => `<button type="button" class="ll-btn ll-btn-outline ll-btn-sm">${action}</button>`).join('') +
                        '</div>';
                }
            }

            modalBody.innerHTML = '<div class="ll-table-detail-list">' +
                fields.map(f =>
                    `<div class="ll-table-detail-item"><div class="ll-table-detail-label">${f.label}</div><div class="ll-table-detail-value">${f.value}</div></div>`
                ).join('') +
                '</div>' + actionsHtml;
            modal.show();
        });
    });
})();
