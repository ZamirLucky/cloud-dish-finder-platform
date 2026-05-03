// upload-create script drives the Upload/Create menu page:
// 1) POST to "start upload" to create a Menu and get a menuId
// 2) POST each selected image to "upload single" with progress indicators

document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("uploadForm");
    if (!form) return;

    const messageArea = document.getElementById("messageArea");
    const progressContainer = document.getElementById("progressContainer");

    // Endpoints are provided by the Razor view via data-* attributes
    const startUploadUrl = form.dataset.startUploadUrl;
    const uploadSingleUrl = form.dataset.uploadSingleUrl;

    // Show a Bootstrap alert in the message area
    function showMessage(message, type) {
        messageArea.innerHTML = `<div class="alert alert-${type}">${message}</div>`;
    }

    // Create UI elements for displaying per-file upload progress
    function createProgressRow(fileName) {
        const wrapper = document.createElement("div");
        wrapper.className = "mb-3";

        wrapper.innerHTML = `
            <div><strong>${fileName}</strong></div>
            <div class="progress" style="height: 24px;">
                <div class="progress-bar" role="progressbar" style="width: 0%">0%</div>
            </div>
            <div class="small mt-1 status-text">Waiting...</div>
        `;

        progressContainer.appendChild(wrapper);

        return {
            bar: wrapper.querySelector(".progress-bar"),
            status: wrapper.querySelector(".status-text")
        };
    }

    // Step 1: create the menu record server-side and return its menuId.
    // The anti-forgery token is included because the endpoint is a POST.
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
            throw new Error("Failed to create menu before uploading images.");
        }

        const result = await response.json();

        if (!result.success) {
            throw new Error(result.message || "Failed to create menu.");
        }

        return result.menuId;
    }

    // Step 2: upload one image with progress.
    // XHR is used instead of fetch because upload progress is widely supported via xhr.upload.onprogress.
    function uploadSingleFile(file, restaurantId, menuId, token, progressUi) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open("POST", uploadSingleUrl);

            xhr.upload.onprogress = function (e) {
                if (e.lengthComputable) {
                    const percent = Math.round((e.loaded / e.total) * 100);
                    progressUi.bar.style.width = percent + "%";
                    progressUi.bar.textContent = percent + "%";
                    progressUi.status.textContent = `Uploading... ${percent}%`;
                }
            };

            xhr.onload = function () {
                if (xhr.status >= 200 && xhr.status < 300) {
                    const result = JSON.parse(xhr.responseText);

                    if (result.success) {
                        progressUi.bar.style.width = "100%";
                        progressUi.bar.textContent = "100%";
                        progressUi.status.textContent = "Completed";
                        resolve(result);
                    } else {
                        progressUi.status.textContent = result.message || "Failed";
                        reject(new Error(result.message || "Upload failed."));
                    }
                } else {
                    progressUi.status.textContent = "Failed";
                    reject(new Error("Upload failed."));
                }
            };

            xhr.onerror = function () {
                progressUi.status.textContent = "Network error";
                reject(new Error("Network error during upload."));
            };

            // Multipart form POST expected by the ASP.NET endpoint
            const formData = new FormData();
            formData.append("RestaurantId", restaurantId);
            formData.append("MenuId", menuId);
            formData.append("File", file);
            formData.append("__RequestVerificationToken", token);

            xhr.send(formData);
        });
    }

    // Submit handler: validate inputs, create the menu, then upload each image sequentially.
    form.addEventListener("submit", async function (e) {
        e.preventDefault();

        messageArea.innerHTML = "";
        progressContainer.innerHTML = "";

        const restaurantId = document.getElementById("RestaurantId")?.value;
        const menuTitle = document.getElementById("MenuTitle")?.value;
        const files = document.getElementById("menuImages")?.files;

        // ASP.NET anti-forgery token generated in the form
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
            showMessage("Please select at least one image.", "danger");
            return;
        }

        if (!token) {
            showMessage("Missing anti-forgery token.", "danger");
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

            showMessage(`${successCount} image(s) uploaded successfully.`, "success");
            form.reset();
        } catch (error) {
            showMessage(error.message || "Unexpected upload error.", "danger");
        }
    });
});