import React, { useState, useEffect, useRef, useCallback } from "react";

function formatCameraLabel(d, i) {
  const label = (d?.label || "").trim();
  const lower = label.toLowerCase();
  if (lower.includes("eye")) {
    return `👓 XREAL Eye (Glasses Camera)`;
  }
  if (lower.includes("xreal") && !lower.includes("beam")) {
    return `👓 XREAL One Pro Camera`;
  }
  if (lower.includes("uvc") || lower.includes("usb") || lower.includes("external")) {
    return `👓 External Glasses Camera (${label || "USB/UVC"})`;
  }
  if (lower.includes("back") || lower.includes("rear") || lower.includes("environment")) {
    return `📱 Beam/Phone Rear Camera`;
  }
  if (lower.includes("front") || lower.includes("user")) {
    return `📱 Beam/Phone Front Camera`;
  }
  return label ? `📷 ${label}` : `Camera ${i + 1}`;
}

// A camera is "built-in" if its label clearly says front/back/rear/user/environment/beam.
function isBuiltInCamera(d) {
  const l = (d?.label || "").toLowerCase();
  return (
    l.includes("front") || l.includes("back") || l.includes("rear") ||
    l.includes("user") || l.includes("environment") || l.includes("beam")
  );
}

// External = XREAL / UVC / USB / External / glass, OR any device that is not clearly built-in.
function isExternalCamera(d) {
  const l = (d?.label || "").toLowerCase();
  if (l.includes("eye") || l.includes("xreal") || l.includes("uvc") || l.includes("usb") ||
      l.includes("external") || l.includes("glass")) {
    return !l.includes("beam");
  }
  return !isBuiltInCamera(d);
}

function pickBestCamera(videoInputs) {
  const byLabel =
    videoInputs.find(d => (d.label || "").toLowerCase().includes("eye")) ||
    videoInputs.find(isExternalCamera);
  if (byLabel) return byLabel;

  // With "Capture with Glasses" ON the glasses can show up as a secondary / virtual camera.
  // Prefer the highest-index device that is not the primary front sensor.
  const nonFront = videoInputs.filter(d => {
    const l = (d.label || "").toLowerCase();
    return !l.includes("front") && !l.includes("user");
  });
  if (nonFront.length > 1) return nonFront[nonFront.length - 1];
  return nonFront[0] || videoInputs[0];
}

export default function ScannerModal({ isOpen, onClose, onComplete, addLog }) {
  const [videoDevices, setVideoDevices] = useState([]);
  const [selectedDeviceId, setSelectedDeviceId] = useState("");
  const [stream, setStream] = useState(null);
  const [capturedPages, setCapturedPages] = useState([]); // [{ id, blob, previewUrl }]
  const [currentSnapshot, setCurrentSnapshot] = useState(null); // { blob, previewUrl } | null
  const [processing, setProcessing] = useState(false);
  const [processingStep, setProcessingStep] = useState("");
  const [cameraError, setCameraError] = useState(null);
  const [isInsecureContext, setIsInsecureContext] = useState(false);
  const [retryCount, setRetryCount] = useState(0);
  // Stream must not start until device enumeration finished (prevents two concurrent getUserMedia calls)
  const [camerasReady, setCamerasReady] = useState(false);
  // Bumped to force a fresh getUserMedia when the track dies (USB unplug, tab suspended, OS reclaim)
  const [streamNonce, setStreamNonce] = useState(0);

  const videoRef = useRef(null);
  const streamRef = useRef(null);
  const streamStateRef = useRef(null);

  // Keep streamRef in sync with stream state
  useEffect(() => {
    streamRef.current = stream;
    streamStateRef.current = stream;
  }, [stream]);

  const attachStream = useCallback((video, s) => {
    if (!video) return;
    if (!s) {
      video.srcObject = null;
      return;
    }
    if (video.srcObject !== s) video.srcObject = s;
    video.playsInline = true;
    video.muted = true;
    const p = video.play();
    if (p !== undefined) p.catch(() => {});
  }, []);

  // Callback ref: the <video> unmounts while an error panel is shown and remounts on retry.
  // Re-attach the live stream every time the element is (re)created, otherwise it stays black.
  const setVideoEl = useCallback((el) => {
    videoRef.current = el;
    if (el) attachStream(el, streamStateRef.current);
  }, [attachStream]);

  // Keep video element playing the active stream
  useEffect(() => {
    attachStream(videoRef.current, stream);
    if (!stream) return;
    const t = setTimeout(() => attachStream(videoRef.current, stream), 150);
    return () => clearTimeout(t);
  }, [stream, currentSnapshot, isOpen, attachStream]);

  // Self-heal: restart the stream if the camera track ends or the tab returns from background
  useEffect(() => {
    if (!isOpen || !stream) return;
    const tracks = stream.getVideoTracks();
    const onEnded = () => setStreamNonce(n => n + 1);
    tracks.forEach(t => t.addEventListener("ended", onEnded));
    const onVisible = () => {
      if (document.visibilityState !== "visible") return;
      const live = stream.getVideoTracks().some(t => t.readyState === "live");
      if (!live) setStreamNonce(n => n + 1);
      else attachStream(videoRef.current, stream);
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      tracks.forEach(t => t.removeEventListener("ended", onEnded));
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [isOpen, stream, attachStream]);

  // Release the (already stopped) stream when the modal closes so reopening never reuses a dead stream
  useEffect(() => {
    if (!isOpen) {
      setStream(null);
      setCamerasReady(false);
    }
  }, [isOpen]);

  // Watchdog: If camera view is visible and video paused unexpectedly, auto-resume
  useEffect(() => {
    if (!isOpen || !stream || currentSnapshot) return;
    const interval = setInterval(() => {
      const v = videoRef.current;
      if (v && v.paused && v.srcObject) {
        v.play().catch(() => {});
      }
    }, 800);
    return () => clearInterval(interval);
  }, [isOpen, stream, currentSnapshot]);

  // 1. Enumerate video devices and select the smart glasses / external camera by default
  useEffect(() => {
    if (!isOpen) return;
    setCameraError(null);
    setIsInsecureContext(false);
    setCamerasReady(false);

    let active = true;

    async function initCameras() {
      try {
        // Detect insecure HTTP context — browsers block Camera API on plain http:// (non-localhost)
        if (!window.isSecureContext) {
          if (active) {
            setIsInsecureContext(true);
            setCameraError("insecure_context");
          }
          return;
        }

        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {
          throw new Error(
            "Camera API unavailable. This feature requires a secure (HTTPS) connection " +
            "and camera permissions to be granted."
          );
        }

        setIsInsecureContext(false);

        // Check if devices already have labels (permission already granted in WebView / browser)
        let devices = await navigator.mediaDevices.enumerateDevices();
        const hasLabels = devices.some(d => d.kind === "videoinput" && !!d.label);

        // Only probe if enumerateDevices() has NO labels (first-time permission prompt)
        if (!hasLabels) {
          let probe = null;
          try {
            probe = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
          } catch (_) {}
          if (probe) {
            probe.getTracks().forEach(t => t.stop());
            // Wait 250ms for Android camera service to fully release the hardware sensor
            await new Promise(r => setTimeout(r, 250));
          }
          devices = await navigator.mediaDevices.enumerateDevices();
        }

        // Keep EVERY videoinput (including unlabeled / unidentified USB ones).
        const videoInputs = devices.filter(d => d.kind === "videoinput");
        if (!active) return;

        setVideoDevices(videoInputs);

        const chosen = pickBestCamera(videoInputs);
        if (chosen) {
          // Deviceids can change after a refresh; always re-pick from the fresh list.
          setSelectedDeviceId(chosen.deviceId);
        } else {
          setSelectedDeviceId("");
        }
        setCamerasReady(true);
      } catch (err) {
        if (!active) return;
        setCameraError(err.message || "Failed to initialize camera devices.");
      }
    }

    initCameras();

    // Auto-refresh when USB glasses are plugged / unplugged (no permission prompt, no stream restart).
    async function onDeviceChange() {
      try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        if (!active) return;
        const videoInputs = devices.filter(d => d.kind === "videoinput");
        setVideoDevices(prev => {
          const prevIds = new Set(prev.map(p => p.deviceId));
          const newExternal = videoInputs.find(d => !prevIds.has(d.deviceId) && isExternalCamera(d));
          if (newExternal) setSelectedDeviceId(newExternal.deviceId);
          return videoInputs;
        });
      } catch (_) {}
    }
    if (navigator.mediaDevices && navigator.mediaDevices.addEventListener) {
      navigator.mediaDevices.addEventListener("devicechange", onDeviceChange);
    }

    return () => {
      active = false;
      if (navigator.mediaDevices && navigator.mediaDevices.removeEventListener) {
        navigator.mediaDevices.removeEventListener("devicechange", onDeviceChange);
      }
    };
  }, [isOpen, retryCount]);

  // 2. Start stream whenever selectedDeviceId changes
  useEffect(() => {
    if (!isOpen || !camerasReady) return;

    let active = true;
    let localStream = null;

    async function startStream() {
      try {
        setCameraError(null);

        // Cleanly stop any existing stream and wait for camera HAL release
        if (streamRef.current) {
          streamRef.current.getTracks().forEach(t => t.stop());
          streamRef.current = null;
          await new Promise(r => setTimeout(r, 200));
        }

        // Guard: navigator.mediaDevices is undefined in non-HTTPS contexts or
        // in WebViews that haven't been granted camera permissions.
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
          setCameraError(
            "Camera API unavailable. Make sure the app is running over HTTPS " +
            "and that camera permissions are granted."
          );
          return;
        }

        // Never force facingMode when a specific device is chosen (USB/UVC cameras have none).
        if (selectedDeviceId) {
          try {
            localStream = await navigator.mediaDevices.getUserMedia({
              video: { deviceId: { exact: selectedDeviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } },
              audio: false
            });
          } catch (firstErr) {
            if (firstErr.name === "NotAllowedError" || firstErr.name === "PermissionDeniedError") throw firstErr;
            try {
              // External cameras often reject resolution hints: retry with the bare exact deviceId.
              localStream = await navigator.mediaDevices.getUserMedia({
                video: { deviceId: { exact: selectedDeviceId } },
                audio: false
              });
            } catch (_) {
              // Fallback if deviceId was remapped
              localStream = await navigator.mediaDevices.getUserMedia({
                video: { deviceId: { ideal: selectedDeviceId } },
                audio: false
              });
            }
          }
        } else {
          localStream = await navigator.mediaDevices.getUserMedia({
            video: { width: { ideal: 1920 }, height: { ideal: 1080 } },
            audio: false
          });
        }
        if (!active) {
          localStream.getTracks().forEach(t => t.stop());
          return;
        }

        streamRef.current = localStream;
        setStream(localStream);
      } catch (err) {
        if (!active) return;
        const msg = err.message || "";
        // Provide actionable messages for common permission / context errors
        if (err.name === "NotAllowedError" || err.name === "PermissionDeniedError") {
          setCameraError("Camera permission denied. Please allow camera access and try again.");
        } else if (err.name === "NotFoundError" || err.name === "DevicesNotFoundError") {
          setCameraError("No camera found. Connect your XREAL Eye glasses and try again.");
        } else if (err.name === "NotReadableError" || err.name === "TrackStartError") {
          setCameraError("Camera is in use by another app. Close other apps and try again.");
        } else {
          setCameraError("Could not access camera feed. " + msg);
        }
      }
    }

    startStream();

    return () => {
      active = false;
      if (localStream) {
        localStream.getTracks().forEach(t => t.stop());
      }
    };
  }, [isOpen, selectedDeviceId, camerasReady, streamNonce]);

  const allUrlsRef = useRef(new Set());

  const registerUrl = useCallback((url) => {
    allUrlsRef.current.add(url);
    return url;
  }, []);

  const revokeUrl = useCallback((url) => {
    if (url && allUrlsRef.current.has(url)) {
      URL.revokeObjectURL(url);
      allUrlsRef.current.delete(url);
    }
  }, []);

  // Clean up all preview URLs only when modal closes or unmounts
  useEffect(() => {
    if (!isOpen) {
      allUrlsRef.current.forEach(u => URL.revokeObjectURL(u));
      allUrlsRef.current.clear();
    }
    return () => {
      allUrlsRef.current.forEach(u => URL.revokeObjectURL(u));
      allUrlsRef.current.clear();
    };
  }, [isOpen]);

  // Snap photo from live video stream
  const handleSnap = useCallback(async () => {
    const video = videoRef.current;
    if (!video) return;

    if (video.paused) {
      try {
        await video.play();
      } catch (_) {}
    }

    const width = video.videoWidth || 1920;
    const height = video.videoHeight || 1080;

    if (!video.videoWidth || !video.videoHeight) {
      if (addLog) addLog("⚠️ Camera frame not ready yet, retrying...", "warn");
      return;
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.drawImage(video, 0, 0, width, height);

    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.95));
    if (!blob) return;

    const previewUrl = registerUrl(URL.createObjectURL(blob));
    setCurrentSnapshot({ blob, previewUrl, width, height });
    if (addLog) addLog(`📸 Captured frame (${width}×${height})`, "info");
  }, [addLog, registerUrl]);

  // "Add Page" / "Capture Another"
  const handleAddPage = useCallback(() => {
    if (!currentSnapshot) return;

    setCapturedPages(prev => [
      ...prev,
      {
        id: Date.now(),
        blob: currentSnapshot.blob,
        previewUrl: currentSnapshot.previewUrl
      }
    ]);

    if (addLog) addLog(`📄 Page ${capturedPages.length + 1} added to batch`, "info");
    setCurrentSnapshot(null); // return to live viewfinder
  }, [currentSnapshot, capturedPages.length, addLog]);

  // "Retake"
  const handleRetake = useCallback(() => {
    if (currentSnapshot) {
      revokeUrl(currentSnapshot.previewUrl);
      setCurrentSnapshot(null);
    }
  }, [currentSnapshot, revokeUrl]);

  // Remove a page from batch
  const handleRemovePage = useCallback((indexToRemove) => {
    setCapturedPages(prev => {
      const target = prev[indexToRemove];
      if (target) revokeUrl(target.previewUrl);
      return prev.filter((_, idx) => idx !== indexToRemove);
    });
  }, [revokeUrl]);

  // "Complete" / "Process" (Finalize batch and run OCR pipeline)
  const handleProcessBatch = useCallback(async () => {
    const finalPages = [...capturedPages];
    if (currentSnapshot) {
      finalPages.push(currentSnapshot);
    }

    if (finalPages.length === 0) {
      alert("Please capture at least one page before processing.");
      return;
    }

    setProcessing(true);
    setProcessingStep("Uploading batch and applying image preprocessing...");

    try {
      const formData = new FormData();
      finalPages.forEach((page, idx) => {
        formData.append("files", page.blob, `page_${idx + 1}.jpg`);
      });

      const dateStr = new Date().toLocaleDateString();
      const timeStr = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
      const docLabel = `Glasses Scan - ${dateStr} ${timeStr} (${finalPages.length} ${finalPages.length === 1 ? "page" : "pages"})`;
      formData.append("label", docLabel);

      setProcessingStep("Running Tesseract OCR & generating searchable document...");

      const res = await fetch("/api/admin/files/ocr-batch", {
        method: "POST",
        body: formData
      });

      if (!res.ok) {
        const errText = await res.text();
        throw new Error(`OCR processing failed (${res.status}): ${errText}`);
      }

      const data = await res.json();
      if (!data.id) {
        throw new Error("No document ID returned from OCR batch service.");
      }

      if (addLog) addLog(`✨ OCR complete: "${docLabel}" with ${finalPages.length} pages`, "info");

      onClose();
      if (onComplete) {
        onComplete({ id: data.id, name: docLabel, totalPages: finalPages.length });
      }
    } catch (err) {
      console.error("Batch OCR error:", err);
      alert(`Processing error: ${err.message}`);
      if (addLog) addLog(`❌ OCR failed: ${err.message}`, "error");
    } finally {
      setProcessing(false);
      setProcessingStep("");
    }
  }, [capturedPages, currentSnapshot, onClose, onComplete, addLog]);

  if (!isOpen) return null;

  return (
    <div style={styles.overlay}>
      <style>{`
        video::-webkit-media-controls { display: none !important; -webkit-appearance: none !important; }
        video::-webkit-media-controls-enclosure { display: none !important; }
        video::-webkit-media-controls-panel { display: none !important; }
        video::-webkit-media-controls-play-button { display: none !important; -webkit-appearance: none !important; }
        video::-webkit-media-controls-overlay-play-button { display: none !important; -webkit-appearance: none !important; }
        video::-webkit-media-controls-start-playback-button { display: none !important; -webkit-appearance: none !important; }
      `}</style>
      <div style={styles.modal}>
        {/* Top Header (Both Screens) */}
        <div style={styles.header}>
          {/* Line 1: XREAL Eye Document Scanner (keep on a single line) */}
          <div style={styles.headerLine1}>
            <h2 style={styles.titleSingleLine}>XREAL Eye Document Scanner</h2>
            <button
              style={styles.closeBtn}
              onClick={onClose}
              disabled={processing}
              title="Close Scanner"
            >
              ✕
            </button>
          </div>

          {/* Line 2: Device dropdown centered directly below it */}
          {videoDevices.length > 0 && (
            <div style={styles.headerLine2}>
              <select
                style={styles.cameraSelect}
                value={selectedDeviceId}
                onChange={e => setSelectedDeviceId(e.target.value)}
                disabled={processing}
                title="Select Camera Source"
              >
                {videoDevices.map((d, i) => (
                  <option key={d.deviceId || i} value={d.deviceId}>
                    {formatCameraLabel(d, i)}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {/* Viewfinder / Preview Body */}
        <div style={styles.body}>
          {cameraError ? (
            <div style={styles.errorBox}>
              {isInsecureContext ? (
                <>
                  <div style={{ fontSize: "2rem", marginBottom: 10 }}>🔒</div>
                  <div style={{ fontWeight: 700, fontSize: "1rem", marginBottom: 8, color: "#f87171" }}>
                    Camera blocked — HTTP not allowed
                  </div>
                  <div style={{ ...styles.errorSub, textAlign: "left", maxWidth: 340 }}>
                    <p style={{ margin: "0 0 10px", color: "#4ade80", fontWeight: 600 }}>
                      Easiest fix: open the app via the secure <code style={styles.inlineCode}>https://…trycloudflare.com</code> link shown by start.bat — no flags needed.
                    </p>
                    <p style={{ margin: "0 0 10px" }}>Chrome blocks camera on plain <code style={styles.inlineCode}>http://</code> pages. Or fix it on this device in 3 steps:</p>
                    <ol style={{ margin: 0, paddingLeft: 18, lineHeight: 1.8 }}>
                      <li>Open a new tab and go to:<br/>
                        <code style={{ ...styles.inlineCode, display: "block", marginTop: 4, wordBreak: "break-all", userSelect: "all" }}>
                          chrome://flags/#unsafely-treat-insecure-origin-as-secure
                        </code>
                      </li>
                      <li>Paste this URL into the text field:<br/>
                        <code style={{ ...styles.inlineCode, display: "block", marginTop: 4, wordBreak: "break-all", userSelect: "all" }}>
                          {window.location.origin}
                        </code>
                      </li>
                      <li>Set the toggle to <strong>Enabled</strong> → tap <strong>Relaunch</strong></li>
                    </ol>
                    <p style={{ margin: "12px 0 0", color: "#94a3b8", fontSize: "0.8rem" }}>Then come back to this page and try again.</p>
                  </div>
                  <button
                    style={styles.retryBtn}
                    onClick={() => { setCameraError(null); setIsInsecureContext(false); setRetryCount(c => c + 1); }}
                  >
                    🔄 Retry Camera
                  </button>
                </>
              ) : (
                <>
                  <div style={{ fontSize: "2rem", marginBottom: 10 }}>⚠️</div>
                  <div>{cameraError}</div>
                  <div style={styles.errorSub}>
                    Make sure your XREAL Eye is connected via USB-C and camera permissions are allowed.
                  </div>
                  <button
                    style={styles.retryBtn}
                    onClick={() => { setCameraError(null); setRetryCount(c => c + 1); }}
                  >
                    🔄 Retry Camera
                  </button>
                </>
              )}
            </div>
          ) : (
            <>
              {/* Live viewfinder is permanently mounted and kept playing */}
              <div
                style={styles.viewfinderContainer}
                onClick={() => {
                  if (videoRef.current && videoRef.current.paused) {
                    videoRef.current.play().catch(() => {});
                  }
                }}
              >
                <video
                  ref={setVideoEl}
                  autoPlay
                  playsInline
                  webkit-playsinline="true"
                  muted
                  controls={false}
                  onLoadedMetadata={e => e.target.play().catch(() => {})}
                  onCanPlay={e => e.target.play().catch(() => {})}
                  onPause={e => {
                    if (e.target.srcObject && !currentSnapshot) {
                      e.target.play().catch(() => {});
                    }
                  }}
                  style={styles.videoStream}
                />

                {!currentSnapshot && (
                  <div style={styles.alignmentGuide}>
                    <div style={styles.cornerTL} />
                    <div style={styles.cornerTR} />
                    <div style={styles.cornerBL} />
                    <div style={styles.cornerBR} />
                    <div style={styles.alignmentPrompt}>
                      Align document page inside frame
                    </div>
                  </div>
                )}
              </div>

              {/* Preview screen overlay: stacks directly on top to fill all reclaimed vertical room */}
              {currentSnapshot && (
                <div style={styles.previewContainer}>
                  <img
                    src={currentSnapshot.previewUrl}
                    alt="Captured document page"
                    style={styles.previewImage}
                  />
                  <div style={styles.previewBadge}>
                    Page {capturedPages.length + 1} Preview (Ready)
                  </div>
                </div>
              )}
            </>
          )}

          {processing && (
            <div style={styles.processingOverlay}>
              <div style={styles.spinner} />
              <div style={styles.processingTitle}>Processing Document Batch</div>
              <div style={styles.processingStepText}>{processingStep}</div>
            </div>
          )}
        </div>

        {/* Multi-Page Tray (Thumbnails of added pages) - shown only in viewfinder mode to maximize preview space */}
        {capturedPages.length > 0 && !processing && !currentSnapshot && (
          <div style={styles.tray}>
            <div style={styles.trayHeader}>
              <span>Batch Pages ({capturedPages.length})</span>
            </div>
            <div style={styles.thumbnailsScroll}>
              {capturedPages.map((page, idx) => (
                <div key={page.id} style={styles.thumbWrapper}>
                  <img src={page.previewUrl} alt={`Page ${idx + 1}`} style={styles.thumbImg} />
                  <span style={styles.thumbIndex}>p.{idx + 1}</span>
                  <button
                    style={styles.thumbRemove}
                    onClick={() => handleRemovePage(idx)}
                    title="Remove page"
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Footer / Controls */}
        <div style={styles.footer}>
          {!currentSnapshot ? (
            <div style={styles.controlRowCenter}>
              <button
                style={styles.shutterBtn}
                onClick={handleSnap}
                disabled={processing || !!cameraError}
                title="Tap to capture page"
              >
                <div style={styles.shutterInner} />
              </button>

              <div style={styles.shutterInfoCol}>
                <span style={styles.shutterLabel}>Tap to Capture</span>
                {capturedPages.length > 0 && !processing && (
                  <button
                    style={styles.finishBtnCompact}
                    onClick={handleProcessBatch}
                  >
                    ✓ Complete ({capturedPages.length})
                  </button>
                )}
              </div>
            </div>
          ) : (
            <div style={styles.previewButtonRow}>
              <button
                style={styles.previewBtnAdd}
                onClick={handleAddPage}
                disabled={processing}
                title="Add Page to batch"
              >
                <span style={styles.btnIcon}>+</span>
                <span style={styles.btnText}>(Add Page)</span>
              </button>

              <button
                style={styles.previewBtnComplete}
                onClick={handleProcessBatch}
                disabled={processing}
                title="Complete document batch"
              >
                <span style={styles.btnIcon}>✓</span>
                <span style={styles.btnText}>(Complete)</span>
              </button>

              <button
                style={styles.previewBtnRetake}
                onClick={handleRetake}
                disabled={processing}
                title="Retake photo"
              >
                <span style={styles.btnIcon}>↺</span>
                <span style={styles.btnText}>(Retake)</span>
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

const styles = {
  overlay: {
    position: "fixed",
    inset: 0,
    background: "#000000",
    zIndex: 9999,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: 0,
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
  },
  modal: {
    width: "100%",
    maxWidth: "100%",
    height: "100%",
    maxHeight: "100%",
    background: "#05080e",
    border: "none",
    borderRadius: 0,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden"
  },
  header: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    padding: "6px 12px 6px",
    borderBottom: "1px solid #141e2b",
    background: "#0d1520",
    flexShrink: 0,
    width: "100%"
  },
  headerLine1: {
    position: "relative",
    width: "100%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "26px"
  },
  titleSingleLine: {
    margin: 0,
    fontSize: "0.92rem",
    fontWeight: 700,
    color: "#f0f4f8",
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
    textAlign: "center",
    paddingLeft: "30px",
    paddingRight: "30px"
  },
  closeBtn: {
    position: "absolute",
    right: 0,
    top: "50%",
    transform: "translateY(-50%)",
    background: "transparent",
    border: "none",
    color: "#94a3b8",
    fontSize: "1.15rem",
    cursor: "pointer",
    padding: "2px 6px",
    borderRadius: "4px",
    lineHeight: 1
  },
  headerLine2: {
    width: "100%",
    display: "flex",
    justifyContent: "center",
    marginTop: "3px"
  },
  cameraSelect: {
    background: "#080d14",
    color: "#e2e8f0",
    border: "1px solid #1f3044",
    borderRadius: "6px",
    padding: "3px 8px",
    fontSize: "0.75rem",
    outline: "none",
    maxWidth: "260px",
    textAlign: "center"
  },
  body: {
    flex: 1,
    minHeight: 0,
    position: "relative",
    background: "#000000",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden",
    width: "100%",
    height: "100%"
  },
  viewfinderContainer: {
    width: "100%",
    height: "100%",
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    background: "#000000",
    overflow: "hidden"
  },
  videoStream: {
    width: "100%",
    height: "100%",
    objectFit: "contain",
    display: "block"
  },
  alignmentGuide: {
    position: "absolute",
    width: "92%",
    height: "92%",
    border: "1px dashed rgba(56, 189, 248, 0.45)",
    borderRadius: "10px",
    pointerEvents: "none",
    display: "flex",
    alignItems: "flex-end",
    justifyContent: "center",
    paddingBottom: "10px"
  },
  cornerTL: {
    position: "absolute",
    top: "-2px",
    left: "-2px",
    width: "24px",
    height: "24px",
    borderTop: "3px solid #38bdf8",
    borderLeft: "3px solid #38bdf8",
    borderTopLeftRadius: "6px"
  },
  cornerTR: {
    position: "absolute",
    top: "-2px",
    right: "-2px",
    width: "24px",
    height: "24px",
    borderTop: "3px solid #38bdf8",
    borderRight: "3px solid #38bdf8",
    borderTopRightRadius: "6px"
  },
  cornerBL: {
    position: "absolute",
    bottom: "-2px",
    left: "-2px",
    width: "24px",
    height: "24px",
    borderBottom: "3px solid #38bdf8",
    borderLeft: "3px solid #38bdf8",
    borderBottomLeftRadius: "6px"
  },
  cornerBR: {
    position: "absolute",
    bottom: "-2px",
    right: "-2px",
    width: "24px",
    height: "24px",
    borderBottom: "3px solid #38bdf8",
    borderRight: "3px solid #38bdf8",
    borderBottomRightRadius: "6px"
  },
  alignmentPrompt: {
    background: "rgba(15, 23, 42, 0.8)",
    color: "#e2e8f0",
    padding: "4px 10px",
    borderRadius: "14px",
    fontSize: "0.72rem",
    letterSpacing: "0.4px"
  },
  previewContainer: {
    position: "absolute",
    inset: 0,
    zIndex: 10,
    width: "100%",
    height: "100%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    background: "#000000",
    overflow: "hidden"
  },
  previewImage: {
    width: "100%",
    height: "100%",
    maxWidth: "100%",
    maxHeight: "100%",
    objectFit: "contain",
    display: "block"
  },
  previewBadge: {
    position: "absolute",
    top: "8px",
    left: "8px",
    background: "rgba(37, 99, 235, 0.9)",
    color: "#fff",
    padding: "3px 8px",
    borderRadius: "4px",
    fontSize: "0.72rem",
    fontWeight: 600,
    zIndex: 15
  },
  processingOverlay: {
    position: "absolute",
    inset: 0,
    background: "rgba(10, 15, 24, 0.92)",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: "14px",
    zIndex: 20
  },
  spinner: {
    width: "48px",
    height: "48px",
    borderRadius: "50%",
    border: "4px solid #1e293b",
    borderTopColor: "#38bdf8",
    animation: "spin 1s linear infinite"
  },
  processingTitle: {
    fontSize: "1.1rem",
    fontWeight: 700,
    color: "#f8fafc"
  },
  processingStepText: {
    fontSize: "0.85rem",
    color: "#94a3b8"
  },
  tray: {
    background: "#080c14",
    borderTop: "1px solid #141e2b",
    padding: "3px 8px",
    flexShrink: 0
  },
  trayHeader: {
    fontSize: "0.65rem",
    color: "#94a3b8",
    marginBottom: "2px",
    textTransform: "uppercase",
    letterSpacing: "0.5px",
    fontWeight: 600
  },
  thumbnailsScroll: {
    display: "flex",
    gap: "6px",
    overflowX: "auto",
    paddingBottom: "2px"
  },
  thumbWrapper: {
    position: "relative",
    width: "36px",
    height: "48px",
    borderRadius: "4px",
    overflow: "hidden",
    border: "1px solid #28374a",
    flexShrink: 0
  },
  thumbImg: {
    width: "100%",
    height: "100%",
    objectFit: "cover"
  },
  thumbIndex: {
    position: "absolute",
    bottom: 1,
    left: 1,
    background: "rgba(0,0,0,0.75)",
    color: "#fff",
    fontSize: "0.55rem",
    padding: "1px 2px",
    borderRadius: "2px"
  },
  thumbRemove: {
    position: "absolute",
    top: 1,
    right: 1,
    background: "rgba(239, 68, 68, 0.85)",
    color: "#fff",
    border: "none",
    width: "14px",
    height: "14px",
    borderRadius: "50%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    fontSize: "0.5rem",
    cursor: "pointer"
  },
  footer: {
    padding: "6px 10px",
    background: "#0d1520",
    borderTop: "1px solid #141e2b",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    flexShrink: 0
  },
  controlRowCenter: {
    display: "flex",
    alignItems: "center",
    gap: "10px",
    justifyContent: "center"
  },
  shutterBtn: {
    width: "48px",
    height: "48px",
    borderRadius: "50%",
    border: "3px solid #38bdf8",
    background: "transparent",
    cursor: "pointer",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: 0,
    transition: "transform 0.15s"
  },
  shutterInner: {
    width: "34px",
    height: "34px",
    borderRadius: "50%",
    background: "#38bdf8"
  },
  shutterInfoCol: {
    display: "flex",
    flexDirection: "column",
    gap: "2px"
  },
  shutterLabel: {
    color: "#94a3b8",
    fontSize: "0.72rem",
    fontWeight: 500
  },
  finishBtnCompact: {
    background: "#10b981",
    border: "none",
    color: "#ffffff",
    padding: "4px 10px",
    borderRadius: "12px",
    fontSize: "0.7rem",
    fontWeight: 600,
    cursor: "pointer",
    whiteSpace: "nowrap"
  },
  previewButtonRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: "6px",
    width: "100%",
    maxWidth: "460px",
    margin: "0 auto"
  },
  previewBtnAdd: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    gap: "4px",
    padding: "9px 4px",
    borderRadius: "8px",
    border: "1px solid #2563eb",
    background: "#1d4ed8",
    color: "#ffffff",
    fontSize: "0.76rem",
    fontWeight: 600,
    cursor: "pointer",
    whiteSpace: "nowrap"
  },
  previewBtnComplete: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    gap: "4px",
    padding: "9px 4px",
    borderRadius: "8px",
    border: "none",
    background: "#10b981",
    color: "#ffffff",
    fontSize: "0.76rem",
    fontWeight: 700,
    cursor: "pointer",
    whiteSpace: "nowrap"
  },
  previewBtnRetake: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    gap: "4px",
    padding: "9px 4px",
    borderRadius: "8px",
    border: "1px solid #475569",
    background: "#1e293b",
    color: "#cbd5e1",
    fontSize: "0.76rem",
    fontWeight: 600,
    cursor: "pointer",
    whiteSpace: "nowrap"
  },
  btnIcon: {
    fontSize: "0.95rem",
    fontWeight: "bold",
    lineHeight: 1
  },
  btnText: {
    fontSize: "0.76rem",
    fontWeight: 600
  },
  errorBox: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: "24px 20px",
    textAlign: "center",
    color: "#f87171",
    fontSize: "0.9rem",
    fontWeight: 500,
    gap: "4px",
    maxWidth: "420px",
    width: "100%"
  },
  errorSub: {
    color: "#94a3b8",
    fontSize: "0.82rem",
    marginTop: "8px",
    lineHeight: 1.6,
    textAlign: "center"
  },
  retryBtn: {
    marginTop: "16px",
    padding: "10px 22px",
    borderRadius: "8px",
    border: "1px solid #38bdf8",
    background: "transparent",
    color: "#38bdf8",
    fontSize: "0.85rem",
    fontWeight: 600,
    cursor: "pointer"
  },
  inlineCode: {
    background: "#1e293b",
    color: "#7dd3fc",
    padding: "2px 6px",
    borderRadius: "4px",
    fontSize: "0.78rem",
    fontFamily: "monospace"
  }
};