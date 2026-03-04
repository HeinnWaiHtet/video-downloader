const HOST_NAME = "com.authorized.downloader.host";

const log = async (entry) => {
  const store = await chrome.storage.local.get({ logs: [] });
  const logs = store.logs;
  logs.push({ at: new Date().toISOString(), ...entry });
  if (logs.length > 300) {
    logs.shift();
  }

  await chrome.storage.local.set({ logs });
};

const requestHost = (type, payload, requestId = crypto.randomUUID()) => {
  return new Promise((resolve, reject) => {
    chrome.runtime.sendNativeMessage(HOST_NAME, { type, payload, requestId }, async (response) => {
      if (chrome.runtime.lastError) {
        await log({ type: "host_error", message: chrome.runtime.lastError.message, requestType: type });
        reject(new Error(chrome.runtime.lastError.message));
        return;
      }

      await log({ type: "host_response", requestType: type, response });
      resolve(response);
    });
  });
};

const hostErrorMessage = (response, fallback) => {
  if (!response) {
    return fallback;
  }

  if (response.type === "error") {
    return response?.payload?.message || response?.payload?.error || fallback;
  }

  return response?.payload?.error || response?.payload?.reason || response?.payload?.message || fallback;
};

chrome.runtime.onInstalled.addListener(async () => {
  await chrome.storage.local.set({
    hostName: HOST_NAME,
    enabledSites: { youtube: true, facebook: true },
    logs: []
  });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "contentScriptLoaded") {
    log({
      type: "content_loaded",
      tabUrl: message.payload?.url || sender?.tab?.url || "",
      host: message.payload?.host || "",
      title: message.payload?.title || ""
    });
    sendResponse({ ok: true });
    return false;
  }

  if (message?.type === "prepareDownloadDialog") {
    (async () => {
      try {
        const detect = await requestHost("detect", {
          url: message.payload.url,
          pageTitle: message.payload.pageTitle
        });

        if (detect?.type !== "detectResponse" || !detect?.payload?.supported) {
          sendResponse({ ok: false, error: hostErrorMessage(detect, "Unsupported page/video.") });
          return;
        }

        sendResponse({ ok: true, site: detect.payload.site, mediaInfo: detect.payload.mediaInfo });
      } catch (error) {
        sendResponse({ ok: false, error: error?.message || "Failed to prepare download dialog." });
      }
    })();

    return true;
  }

  if (message?.type === "pickFolder") {
    (async () => {
      try {
        const pick = await requestHost("pickFolder", { initialPath: message.payload?.initialPath || null });
        if (pick?.type !== "pickFolderResponse" || !pick?.payload?.success) {
          sendResponse({ ok: false, error: hostErrorMessage(pick, "Folder selection failed.") });
          return;
        }

        sendResponse({ ok: true, path: pick.payload.path });
      } catch (error) {
        sendResponse({ ok: false, error: error?.message || "Folder selection failed." });
      }
    })();

    return true;
  }

  if (message?.type === "startDownloadWithOptions") {
    (async () => {
      try {
        await log({
          type: "start_download_request",
          payload: {
            url: message.payload.url,
            site: message.payload.site,
            selectedFormatId: message.payload.selectedFormatId,
            outputPath: message.payload.outputPath,
            filenameTemplate: message.payload.filenameTemplate
          }
        });

        const start = await requestHost("startDownload", {
          url: message.payload.url,
          site: message.payload.site,
          selectedFormatId: message.payload.selectedFormatId,
          outputPath: message.payload.outputPath,
          filenameTemplate: message.payload.filenameTemplate,
          waitForCompletion: true
        });

        await log({ type: "start_download_host_result", response: start });

        if (start?.type !== "startDownloadResponse" || !start?.payload?.accepted) {
          sendResponse({ ok: false, error: hostErrorMessage(start, "Host rejected download.") });
          return;
        }

        await log({ type: "start_download_success", downloadId: start.payload.downloadId, files: start.payload.files || [] });
        sendResponse({ ok: true, downloadId: start.payload.downloadId, files: start.payload.files || [] });
      } catch (error) {
        await log({ type: "start_download_exception", message: error?.message || "Unknown start download error." });
        sendResponse({ ok: false, error: error?.message || "Download failed." });
      }
    })();

    return true;
  }

  return false;
});
