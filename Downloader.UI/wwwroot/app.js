const state = {
  site: null,
  mediaInfo: null,
  sessionId: null,
  pollTimer: null,
  isProbing: false,
  lastProbedUrl: ""
};

const urlInput = document.getElementById("urlInput");
const probeStatus = document.getElementById("probeStatus");
const qualitySelect = document.getElementById("qualitySelect");
const nameInput = document.getElementById("nameInput");
const folderInput = document.getElementById("folderInput");
const browseBtn = document.getElementById("browseBtn");
const downloadBtn = document.getElementById("downloadBtn");
const clearBtn = document.getElementById("clearBtn");
const downloadStatus = document.getElementById("downloadStatus");
const logs = document.getElementById("logs");
const resultModal = document.getElementById("resultModal");
const resultTitle = document.getElementById("resultTitle");
const resultMessage = document.getElementById("resultMessage");
const resultOkBtn = document.getElementById("resultOkBtn");

const setLogs = (lines) => {
  logs.textContent = (lines || []).join("\n") || "No logs yet.";
  logs.scrollTop = logs.scrollHeight;
};

const setProbeStatus = (text, isError = false) => {
  probeStatus.textContent = text;
  probeStatus.style.color = isError ? "#b42318" : "";
};

const setDownloadStatus = (text, isError = false) => {
  downloadStatus.textContent = text;
  downloadStatus.style.color = isError ? "#b42318" : "";
};

const setFormats = (formats) => {
  qualitySelect.innerHTML = "";
  const list = Array.isArray(formats) && formats.length > 0
    ? formats
    : [{ id: "best", label: "Best available" }];

  for (const fmt of list) {
    const option = document.createElement("option");
    option.value = fmt.id;
    option.textContent = fmt.label || fmt.id;
    qualitySelect.appendChild(option);
  }
};

const showResultModal = (title, message) => {
  resultTitle.textContent = title;
  resultMessage.textContent = message;
  resultModal.classList.remove("hidden");
};

const hideResultModal = () => {
  resultModal.classList.add("hidden");
};

const stopPolling = () => {
  if (state.pollTimer) {
    clearInterval(state.pollTimer);
    state.pollTimer = null;
  }
};

const clearForm = () => {
  stopPolling();
  state.site = null;
  state.mediaInfo = null;
  state.sessionId = null;
  state.lastProbedUrl = "";
  urlInput.value = "";
  nameInput.value = "";
  folderInput.value = "";
  setFormats([{ id: "best", label: "Best available" }]);
  setProbeStatus("Ready");
  setDownloadStatus("Idle");
  setLogs([]);
  downloadBtn.disabled = false;
  hideResultModal();
};

const loadVideo = async () => {
  const url = (urlInput.value || "").trim();
  if (!url) {
    return;
  }

  if (state.isProbing || state.lastProbedUrl === url) {
    return;
  }

  state.isProbing = true;
  setProbeStatus("Loading video...");
  setDownloadStatus("");

  try {
    const res = await fetch("/api/probe", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ url, pageTitle: null })
    });

    const data = await res.json();
    if (!res.ok || !data.ok) {
      throw new Error(data.error || "Load video failed.");
    }

    state.site = data.site;
    state.mediaInfo = data.mediaInfo;
    state.lastProbedUrl = url;

    setFormats(data.mediaInfo?.formats || []);
    nameInput.value = data.mediaInfo?.title || "video";
    setProbeStatus(`Ready: ${data.mediaInfo?.title || "video"}`);
  } catch (error) {
    state.site = null;
    state.mediaInfo = null;
    setProbeStatus(error.message || "Load video failed.", true);
  } finally {
    state.isProbing = false;
  }
};

const startPolling = () => {
  stopPolling();

  state.pollTimer = setInterval(async () => {
    if (!state.sessionId) {
      return;
    }

    try {
      const res = await fetch(`/api/download-status/${state.sessionId}`);
      const data = await res.json();
      if (!res.ok || !data.ok) {
        throw new Error(data.error || "Status check failed.");
      }

      const percentText = data.percent > 0 ? ` ${data.percent.toFixed(1)}%` : "";
      setDownloadStatus(`${data.state}: ${data.status}${percentText}`, data.state === "Failed");
      setLogs(data.logs || []);

      if (data.state === "Completed") {
        stopPolling();
        downloadBtn.disabled = false;
        const target = Array.isArray(data.files) && data.files.length > 0 ? data.files[0] : (folderInput.value || "Downloads");
        showResultModal("Download Completed", `Saved to:\n${target}`);
      }

      if (data.state === "Failed") {
        stopPolling();
        downloadBtn.disabled = false;
      }
    } catch (error) {
      setDownloadStatus(error.message || "Status polling failed.", true);
    }
  }, 1000);
};

browseBtn.addEventListener("click", async () => {
  browseBtn.disabled = true;
  try {
    const res = await fetch("/api/pick-folder", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ initialPath: folderInput.value || null })
    });

    const data = await res.json();
    if (!res.ok || !data.ok) {
      throw new Error(data.error || "Folder selection failed.");
    }

    folderInput.value = data.path || "";
  } catch (error) {
    setDownloadStatus(error.message || "Folder selection failed.", true);
  } finally {
    browseBtn.disabled = false;
  }
});

downloadBtn.addEventListener("click", async () => {
  if (!state.site) {
    setDownloadStatus("Video not loaded. Leave URL field to load first.", true);
    return;
  }

  const payload = {
    url: (urlInput.value || "").trim(),
    site: state.site,
    selectedFormatId: qualitySelect.value || "best",
    outputPath: (folderInput.value || "").trim(),
    filenameTemplate: (nameInput.value || "").trim() || "video"
  };

  downloadBtn.disabled = true;
  setDownloadStatus("Starting download...");
  setLogs(["[Pending] Starting download request"]);

  try {
    const res = await fetch("/api/download-start", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok || !data.ok) {
      throw new Error(data.error || "Download start failed.");
    }

    state.sessionId = data.sessionId;
    startPolling();
  } catch (error) {
    setDownloadStatus(error.message || "Download failed.", true);
    downloadBtn.disabled = false;
  }
});

clearBtn.addEventListener("click", clearForm);
urlInput.addEventListener("blur", () => {
  state.lastProbedUrl = "";
  void loadVideo();
});
urlInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    e.preventDefault();
    state.lastProbedUrl = "";
    void loadVideo();
  }
});
resultOkBtn.addEventListener("click", hideResultModal);
resultModal.addEventListener("click", (e) => {
  if (e.target === resultModal) {
    hideResultModal();
  }
});

setFormats([{ id: "best", label: "Best available" }]);
setLogs([]);
