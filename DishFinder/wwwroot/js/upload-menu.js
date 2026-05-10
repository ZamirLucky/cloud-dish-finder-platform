// Handles the Upload Menu Images page:
// selected-photo preview, drag/drop support, menu creation, and upload progress.

document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("uploadForm");
    if (!form) return;

    const messageArea = document.getElementById("messageArea");
    const progressContainer = document.getElementById("progressContainer");
    const fileInput = document.getElementById("menuImages");
    const dropzone = document.getElementById("uploadDropzone");
    const selectedFilesCard = document.getElementById("selectedFilesCard");
    const selectedFilesList = document.getElementById("selectedFilesList");
    const selectedFilesSummary = document.getElementById("selectedFilesSummary");
    const clearSelectionBtn = document.getElementById("clearSelectionBtn");

    const startUploadUrl = form.dataset.startUploadUrl;
    const uploadSingleUrl = form.dataset.uploadSingleUrl;

    // Converts file sizes into a readable format for the selected-photo list.
    function formatBytes(bytes) {
        if (!bytes) return "0 KB";

        const units = ["B", "KB", "MB", "GB"];
        let value = bytes;
        let unitIndex = 0;

        while (value >= 1024 && unitIndex < units.length - 1) {
            value /= 1024;
            unitIndex++;
        }

        return `${value.toFixed(value >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
    }

    // Displays the selected menu photos before upload.
    function renderSelectedFiles() {
        if (!fileInput || !selectedFilesCard || !selectedFilesList || !selectedFilesSummary) return;

        const files = Array.from(fileInput.files || []);
        selectedFilesList.innerHTML = "";

        if (files.length === 0) {
            selectedFilesCard.style.display = "none";
            selectedFilesSummary.textContent = "0 photos";
            return;
        }

        const totalBytes = files.reduce((total, file) => total + file.size, 0);
        selectedFilesSummary.textContent =
            `${files.length} ${files.length === 1 ? "photo" : "photos"} · ${formatBytes(totalBytes)}`;

        files.forEach(file => {
            const item = document.createElement("li");

            const name = document.createElement("span");
            name.textContent = file.name;

            const size = document.createElement("span");
            size.className = "file-size-text";
            size.textContent = formatBytes(file.size);

            item.appendChild(name);
            item.appendChild(size);
            selectedFilesList.appendChild(item);
        });

        selectedFilesCard.style.display = "block";
    }

    // Sets up the normal file picker and drag/drop behaviour.
    function initialiseFilePickerUi() {
        if (!fileInput) return;

        fileInput.addEventListener("change", renderSelectedFiles);

        if (clearSelectionBtn) {
            clearSelectionBtn.addEventListener("click", () => {
                window.setTimeout(renderSelectedFiles, 0);
            });
        }

        if (!dropzone) return;

        ["dragenter", "dragover"].forEach(eventName => {
            dropzone.addEventListener(eventName, event => {
                event.preventDefault();
                dropzone.classList.add("is-dragging");
            });
        });

        ["dragleave", "drop"].forEach(eventName => {
            dropzone.addEventListener(eventName, event => {
                event.preventDefault();
                dropzone.classList.remove("is-dragging");
            });
        });

        dropzone.addEventListener("drop", event => {
            if (!event.dataTransfer || !event.dataTransfer.files.length) return;

            fileInput.files = event.dataTransfer.files;
            renderSelectedFiles();
        });
    }

    // Shows Bootstrap-style success or error messages.
    function showMessage(message, type) {
        if (!messageArea) return;
        messageArea.innerHTML = `<div class="alert alert-${type}">${message}</div>`;
    }

    // Creates one progress row for each photo being uploaded.
    function createProgressRow(fileName) {
        const wrapper = document.createElement("div");
        wrapper.className = "upload-progress-row";

        wrapper.innerHTML = `
            <div class="upload-progress-file-name"></div>
            <div class="progress upload-progress-bar-shell">
                <div class="progress-bar upload-progress-bar"
                     role="progressbar"
                     aria-valuemin="0"
                     aria-valuemax="100"
                     aria-valuenow="0">0%</div>
            </div>
            <div class="small mt-1 upload-progress-status">Waiting...</div>
        `;

        wrapper.querySelector(".upload-progress-file-name").textContent = fileName;
        progressContainer.appendChild(wrapper);

        return {
            row: wrapper,
            bar: wrapper.querySelector(".upload-progress-bar"),
            status: wrapper.querySelector(".upload-progress-status")
        };
    }

    // Creates the menu record before uploading the selected images.
    async function startMenuUpload(restaurantId, menuTitle, token) {
        const formData = new FormData();
        formData.append("RestaurantId", restaurantId);
        formData.append("MenuTitle", menuTitle);
        formData.append("__RequestVerificationToken", token);

        const response = await fetch(startUploadUrl, {
            method: "POST",
            body: formData
        });

        if (!response.ok) {
            throw new Error("We could not start the upload. Please try again.");
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message || "We could not start the upload.");
        }

        return result.menuId;
    }

    function setProgress(progressUi, percent, statusText) {
        progressUi.bar.style.width = `${percent}%`;
        progressUi.bar.textContent = `${percent}%`;
        progressUi.bar.setAttribute("aria-valuenow", percent.toString());
        progressUi.status.textContent = statusText;
    }

    // Uploads one photo and updates its progress bar.
    function uploadSingleFile(file, restaurantId, menuId, token, progressUi) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open("POST", uploadSingleUrl);

            xhr.upload.onprogress = event => {
                if (!event.lengthComputable) return;

                const percent = Math.round((event.loaded / event.total) * 100);
                setProgress(progressUi, percent, `Uploading... ${percent}%`);
            };

            xhr.onload = () => {
                if (xhr.status < 200 || xhr.status >= 300) {
                    progressUi.row.classList.add("is-error");
                    progressUi.status.textContent = "Upload failed";
                    reject(new Error("One of the photos could not be uploaded."));
                    return;
                }

                let result;

                try {
                    result = JSON.parse(xhr.responseText);
                } catch {
                    progressUi.row.classList.add("is-error");
                    progressUi.status.textContent = "Upload failed";
                    reject(new Error("The server returned an invalid upload response."));
                    return;
                }

                if (result.success) {
                    progressUi.row.classList.add("is-complete");
                    setProgress(progressUi, 100, "Completed");
                    resolve(result);
                } else {
                    progressUi.row.classList.add("is-error");
                    progressUi.status.textContent = result.message || "Upload failed";
                    reject(new Error(result.message || "One of the photos could not be uploaded."));
                }
            };

            xhr.onerror = () => {
                progressUi.row.classList.add("is-error");
                progressUi.status.textContent = "Network error";
                reject(new Error("A network error occurred during upload."));
            };

            const formData = new FormData();
            formData.append("RestaurantId", restaurantId);
            formData.append("MenuId", menuId);
            formData.append("File", file);
            formData.append("__RequestVerificationToken", token);

            xhr.send(formData);
        });
    }

    // Validates the form, creates the menu, then uploads each selected photo.
    form.addEventListener("submit", async event => {
        event.preventDefault();

        if (messageArea) messageArea.innerHTML = "";
        if (progressContainer) progressContainer.innerHTML = "";

        const restaurantId = document.getElementById("RestaurantId")?.value;
        const menuTitle = document.getElementById("MenuTitle")?.value;
        const files = fileInput?.files;
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        if (!restaurantId) {
            showMessage("Please select a restaurant.", "danger");
            return;
        }

        if (!menuTitle || !menuTitle.trim()) {
            showMessage("Please enter a menu title.", "danger");
            return;
        }

        if (!files || files.length === 0) {
            showMessage("Please select at least one menu photo.", "danger");
            return;
        }

        if (!token) {
            showMessage("The upload form could not be verified. Refresh the page and try again.", "danger");
            return;
        }

        try {
            const menuId = await startMenuUpload(restaurantId, menuTitle, token);
            let successCount = 0;

            for (const file of files) {
                const progressUi = createProgressRow(file.name);
                await uploadSingleFile(file, restaurantId, menuId, token, progressUi);
                successCount++;
            }

            showMessage(
                `${successCount} ${successCount === 1 ? "photo was" : "photos were"} uploaded successfully.`,
                "success"
            );

            form.reset();
            renderSelectedFiles();
        } catch (error) {
            showMessage(error.message || "The upload could not be completed.", "danger");
        }
    });

    initialiseFilePickerUi();
});