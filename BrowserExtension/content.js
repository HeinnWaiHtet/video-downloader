(() => {
  const ROOT_ID = "authorized-video-downloader-root";
  const DIALOG_ID = "authorized-video-downloader-dialog";
  const BUTTON_ID = "authorized-video-downloader-btn";

  try {
    chrome.runtime.sendMessage({
      type: "contentScriptLoaded",
      payload: { url: location.href, host: location.hostname, title: document.title }
    });
  } catch {
    // ignore
  }

  const isSupportedHost = () => /(^|\.)youtube\.com$|(^|\.)youtu\.be$|(^|\.)facebook\.com$/.test(location.hostname);

  const ensureRoot = () => {
    const parent = document.body || document.documentElement;
    if (!parent) {
      return null;
    }

    let root = document.getElementById(ROOT_ID);
    if (!root) {
      root = document.createElement("div");
      root.id = ROOT_ID;
      Object.assign(root.style, {
        position: "fixed",
        top: "16px",
        right: "16px",
        zIndex: "2147483647",
        pointerEvents: "auto"
      });
      parent.appendChild(root);
    }

    if (!root.shadowRoot) {
      root.attachShadow({ mode: "open" });
    }

    return root;
  };

  const showStatus = (msg, error = false) => {
    const root = ensureRoot();
    if (!root?.shadowRoot) {
      return;
    }

    let toast = root.shadowRoot.getElementById("authorized-video-downloader-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.id = "authorized-video-downloader-toast";
      Object.assign(toast.style, {
        marginTop: "8px",
        maxWidth: "320px",
        padding: "8px 10px",
        borderRadius: "8px",
        font: "600 12px/1.3 -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif",
        boxShadow: "0 4px 14px rgba(0,0,0,0.2)"
      });
      root.shadowRoot.appendChild(toast);
    }

    toast.textContent = msg;
    toast.style.background = error ? "#b42318" : "#027a48";
    toast.style.color = "#fff";

    setTimeout(() => {
      if (toast) {
        toast.remove();
      }
    }, 3500);
  };

  const createDialog = (detect) => {
    const root = ensureRoot();
    if (!root?.shadowRoot) {
      return null;
    }

    const existing = root.shadowRoot.getElementById(DIALOG_ID);
    if (existing) {
      existing.remove();
    }

    const wrap = document.createElement("div");
    wrap.id = DIALOG_ID;
    Object.assign(wrap.style, {
      position: "fixed",
      top: "50px",
      right: "16px",
      width: "360px",
      background: "#ffffff",
      color: "#111827",
      border: "1px solid #e5e7eb",
      borderRadius: "12px",
      boxShadow: "0 20px 40px rgba(0,0,0,0.25)",
      padding: "12px",
      font: "500 13px/1.4 -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    });

    const title = document.createElement("div");
    title.textContent = "Download Video";
    title.style.font = "700 14px/1.2 -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif";
    title.style.marginBottom = "10px";

    const formatLabel = document.createElement("label");
    formatLabel.textContent = "Quality";
    formatLabel.style.display = "block";

    const formatSelect = document.createElement("select");
    formatSelect.style.width = "100%";
    formatSelect.style.margin = "4px 0 10px";
    formatSelect.style.padding = "8px";

    const formats = detect?.mediaInfo?.formats || [];
    const usableFormats = Array.isArray(formats) && formats.length > 0
      ? formats
      : [{ id: "best", label: "Best available" }];

    for (const fmt of usableFormats) {
      const option = document.createElement("option");
      option.value = fmt.id;
      option.textContent = fmt.label || fmt.id;
      formatSelect.appendChild(option);
    }

    const nameLabel = document.createElement("label");
    nameLabel.textContent = "File name";
    nameLabel.style.display = "block";

    const nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.value = (document.title || "video").replace(/\s*-\s*YouTube\s*$/i, "").trim() || "video";
    nameInput.style.width = "100%";
    nameInput.style.margin = "4px 0 10px";
    nameInput.style.padding = "8px";

    const dirLabel = document.createElement("label");
    dirLabel.textContent = "Save folder";
    dirLabel.style.display = "block";

    const dirRow = document.createElement("div");
    dirRow.style.display = "flex";
    dirRow.style.gap = "8px";
    dirRow.style.margin = "4px 0 10px";

    const dirInput = document.createElement("input");
    dirInput.type = "text";
    dirInput.placeholder = "Default Downloads folder";
    dirInput.style.flex = "1";
    dirInput.style.padding = "8px";

    const browseBtn = document.createElement("button");
    browseBtn.type = "button";
    browseBtn.textContent = "Browse";
    browseBtn.style.padding = "8px 10px";

    browseBtn.addEventListener("click", async () => {
      browseBtn.disabled = true;
      try {
        const res = await chrome.runtime.sendMessage({
          type: "pickFolder",
          payload: { initialPath: dirInput.value || null }
        });

        if (!res?.ok) {
          showStatus(res?.error || "Unable to open folder picker.", true);
          return;
        }

        dirInput.value = res.path || "";
      } catch (error) {
        showStatus(error?.message || "Folder picker failed.", true);
      } finally {
        browseBtn.disabled = false;
      }
    });

    dirRow.appendChild(dirInput);
    dirRow.appendChild(browseBtn);

    const actions = document.createElement("div");
    actions.style.display = "flex";
    actions.style.gap = "8px";
    actions.style.justifyContent = "flex-end";

    const cancelBtn = document.createElement("button");
    cancelBtn.type = "button";
    cancelBtn.textContent = "Cancel";
    cancelBtn.style.padding = "8px 12px";

    const downloadBtn = document.createElement("button");
    downloadBtn.type = "button";
    downloadBtn.textContent = "Download";
    downloadBtn.style.padding = "8px 12px";
    downloadBtn.style.background = "#0a66c2";
    downloadBtn.style.color = "#fff";
    downloadBtn.style.border = "none";
    downloadBtn.style.borderRadius = "8px";

    cancelBtn.addEventListener("click", () => wrap.remove());

    downloadBtn.addEventListener("click", async () => {
      downloadBtn.disabled = true;
      cancelBtn.disabled = true;
      downloadBtn.textContent = "Downloading...";

      const payload = {
        url: location.href,
        site: detect.site,
        selectedFormatId: formatSelect.value || "best",
        outputPath: dirInput.value || "",
        filenameTemplate: nameInput.value.trim() || "video"
      };

      try {
        const response = await chrome.runtime.sendMessage({ type: "startDownloadWithOptions", payload });
        if (!response?.ok) {
          showStatus(response?.error || "Download failed.", true);
          downloadBtn.disabled = false;
          cancelBtn.disabled = false;
          downloadBtn.textContent = "Download";
          return;
        }

        wrap.remove();
        const fileInfo = Array.isArray(response.files) && response.files.length > 0
          ? `\n${response.files[0]}`
          : "";
        window.alert(`Download completed successfully.${fileInfo}`);
      } catch (error) {
        showStatus(error?.message || "Download failed.", true);
        downloadBtn.disabled = false;
        cancelBtn.disabled = false;
        downloadBtn.textContent = "Download";
      }
    });

    actions.appendChild(cancelBtn);
    actions.appendChild(downloadBtn);

    wrap.appendChild(title);
    wrap.appendChild(formatLabel);
    wrap.appendChild(formatSelect);
    wrap.appendChild(nameLabel);
    wrap.appendChild(nameInput);
    wrap.appendChild(dirLabel);
    wrap.appendChild(dirRow);
    wrap.appendChild(actions);

    root.shadowRoot.appendChild(wrap);
    return wrap;
  };

  const ensureButton = () => {
    if (!isSupportedHost()) {
      return;
    }

    const root = ensureRoot();
    if (!root?.shadowRoot) {
      return;
    }

    let button = root.shadowRoot.getElementById(BUTTON_ID);
    if (!button) {
      const style = document.createElement("style");
      style.textContent = `
        #${BUTTON_ID} {
          background: #0a66c2;
          color: #fff;
          border: none;
          border-radius: 8px;
          font: 700 14px/1.2 -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif;
          padding: 10px 14px;
          cursor: pointer;
          box-shadow: 0 4px 14px rgba(0,0,0,0.25);
        }
      `;

      button = document.createElement("button");
      button.id = BUTTON_ID;
      button.textContent = "Download";

      button.addEventListener("click", async () => {
        button.disabled = true;
        try {
          const detect = await chrome.runtime.sendMessage({
            type: "prepareDownloadDialog",
            payload: { url: location.href, pageTitle: document.title }
          });

          if (!detect?.ok) {
            showStatus(detect?.error || "Cannot prepare download dialog.", true);
            return;
          }

          createDialog(detect);
        } catch (error) {
          showStatus(error?.message || "Cannot prepare download dialog.", true);
        } finally {
          button.disabled = false;
        }
      });

      root.shadowRoot.appendChild(style);
      root.shadowRoot.appendChild(button);
    }
  };

  ensureButton();

  window.addEventListener("yt-navigate-finish", ensureButton);
  window.addEventListener("popstate", ensureButton);
  document.addEventListener("visibilitychange", ensureButton);
  const observer = new MutationObserver(() => ensureButton());
  observer.observe(document.documentElement, { childList: true, subtree: true });
  setInterval(ensureButton, 1500);
})();
