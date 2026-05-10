// Catalog page script
// Handles row translation, loading states, and restoring original menu text

document.addEventListener("DOMContentLoaded", function () {
    const antiForgeryForm = document.getElementById("antiForgeryForm");
    const tokenInput = antiForgeryForm?.querySelector('input[name="__RequestVerificationToken"]');

    const token = tokenInput ? tokenInput.value : "";
    const translateUrl = antiForgeryForm?.dataset.translateUrl || "";

    // Sends one text value to the Catalog translation endpoint
    async function translateText(restaurantId, menuId, text, targetLanguage) {
        if (!text || text.trim() === "" || text.trim() === "-") {
            return null;
        }

        const body = new URLSearchParams();
        body.append("restaurantId", restaurantId);
        body.append("menuId", menuId);
        body.append("itemName", text);
        body.append("targetLanguage", targetLanguage);

        const response = await fetch(translateUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                "RequestVerificationToken": token
            },
            body: body.toString()
        });

        const responseText = await response.text();

        if (!response.ok) {
            throw new Error(responseText || "Translation request failed.");
        }

        if (!responseText) {
            throw new Error("The translation service returned an empty response.");
        }

        let json;

        try {
            json = JSON.parse(responseText);
        } catch {
            throw new Error("The translation service returned an invalid response.");
        }

        if (!json.success) {
            throw new Error(json.message || "Translation failed.");
        }

        return json.translatedText;
    }

    // Updates the translate button and result message for one row
    function setTranslationState(button, resultBox, state, message) {
        const label = button.querySelector(".translate-label");
        const loading = button.querySelector(".translate-loading");

        resultBox.classList.remove("success", "error", "working");

        if (state === "working") {
            button.disabled = true;
            label?.classList.add("d-none");
            loading?.classList.remove("d-none");
            resultBox.classList.add("working");
        } else {
            button.disabled = false;
            label?.classList.remove("d-none");
            loading?.classList.add("d-none");

            if (state === "success") {
                resultBox.classList.add("success");
            }

            if (state === "error") {
                resultBox.classList.add("error");
            }
        }

        resultBox.textContent = message || "";
    }

    // Translates the visible text fields in the selected table row
    document.querySelectorAll(".translate-btn").forEach(button => {
        button.addEventListener("click", async function () {
            if (!translateUrl) {
                alert("Translation URL is missing. Check data-translate-url in Index.cshtml.");
                return;
            }

            const row = button.closest("tr");
            const translateCell = button.closest("td");

            const language = translateCell.querySelector(".language-select")?.value;
            const resultBox = translateCell.querySelector(".translation-result");

            const restaurantId = button.dataset.restaurantId;
            const menuId = button.dataset.menuId;

            const menuTitleElement = row.querySelector(".menu-title-text");
            const sectionElement = row.querySelector(".section-text");
            const itemNameElement = row.querySelector(".item-name-text");
            const descriptionElement = row.querySelector(".description-text");

            const originalMenuTitle = menuTitleElement?.dataset.originalText ?? "";
            const originalSection = sectionElement?.dataset.originalText ?? "";
            const originalItemName = itemNameElement?.dataset.originalText ?? "";
            const originalDescription = descriptionElement?.dataset.originalText ?? "";

            setTranslationState(button, resultBox, "working", "Translating...");

            try {
                const [
                    translatedMenu,
                    translatedSection,
                    translatedItem,
                    translatedDescription
                ] = await Promise.all([
                    translateText(restaurantId, menuId, originalMenuTitle, language),
                    translateText(restaurantId, menuId, originalSection, language),
                    translateText(restaurantId, menuId, originalItemName, language),
                    translateText(restaurantId, menuId, originalDescription, language)
                ]);

                if (translatedMenu && menuTitleElement) {
                    menuTitleElement.textContent = translatedMenu;
                }

                if (translatedSection && sectionElement) {
                    sectionElement.textContent = translatedSection;
                }

                if (translatedItem && itemNameElement) {
                    itemNameElement.textContent = translatedItem;
                }

                if (translatedDescription && descriptionElement) {
                    descriptionElement.textContent = translatedDescription;
                }

                setTranslationState(button, resultBox, "success", "Translated");
            } catch (error) {
                setTranslationState(
                    button,
                    resultBox,
                    "error",
                    "Translation failed: " + error.message
                );
            }
        });
    });

    // Restores the original text stored in data-original-text attributes
    document.querySelectorAll(".reset-btn").forEach(button => {
        button.addEventListener("click", function () {
            const row = button.closest("tr");

            const menuTitleElement = row.querySelector(".menu-title-text");
            const sectionElement = row.querySelector(".section-text");
            const itemNameElement = row.querySelector(".item-name-text");
            const descriptionElement = row.querySelector(".description-text");
            const resultBox = row.querySelector(".translation-result");

            if (menuTitleElement) {
                menuTitleElement.textContent = menuTitleElement.dataset.originalText || "";
            }

            if (sectionElement) {
                sectionElement.textContent = sectionElement.dataset.originalText || "";
            }

            if (itemNameElement) {
                itemNameElement.textContent = itemNameElement.dataset.originalText || "";
            }

            if (descriptionElement) {
                const originalDescription = descriptionElement.dataset.originalText || "";
                descriptionElement.textContent =
                    originalDescription.trim() === "" ? "-" : originalDescription;
            }

            if (resultBox) {
                resultBox.classList.remove("success", "error", "working");
                resultBox.textContent = "";
            }
        });
    });
});