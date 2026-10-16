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

  const videoRef = useRef(null);

  // 1. Enumerate video devices and select the smart glasses / external camera by default
  useEffect(() => {
    if (!isOpen) return;
    setCameraError(null);
    setIsInsecureContext(false);

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

        let initialStream = null;
        try {
          initialStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        } catch (_) {}

        const devices = await navigator.mediaDevices.enumerateDevices();
        if (initialStream) {
          initialStream.getTracks().forEach(t => t.stop());
        }

        const videoInputs = devices.filter(d => d.kind === "videoinput");
        if (!active) return;

        setVideoDevices(videoInputs);

        // 1. Highest priority: XREAL Eye camera accessory on the glasses
        const xrealEyeCamera = videoInputs.find(d => {
          const label = (d.label || "").toLowerCase();
          return label.includes("eye");
        });

        // 2. Second priority: External USB / UVC / XREAL glasses camera (NOT the handheld Beam/phone body)
        const externalGlassesCamera = videoInputs.find(d => {
          const label = (d.label || "").toLowerCase();
          const isExternal = label.includes("uvc") || label.includes("usb") || label.includes("external") || (label.includes("xreal") && !label.includes("beam")) || label.includes("glass");
          const isInternal = label.includes("front") || label.includes("user") || label.includes("beam");
          return isExternal && !isInternal;
        });

        // 3. Fallback: If no external camera is identified yet, check for non-front camera or first device
        const fallbackCamera = videoInputs.find(d => {
          const label = (d.label || "").toLowerCase();
          return !label.includes("front") && !label.includes("user");
        }) || videoInputs[0];

        const chosen = xrealEyeCamera || externalGlassesCamera || fallbackCamera;

        if (chosen) {
          setSelectedDeviceId(chosen.deviceId);
        }
      } catch (err) {
        if (!active) return;
        setCameraError(err.message || "Failed to initialize camera devices.");
      }
    }

    initCameras();

    return () => {
      active = false;
    };
  }, [isOpen, retryCount]);

  // 2. Start stream whenever selectedDeviceId changes
  useEffect(() => {
    if (!isOpen) return;

    let active = true;
    let localStream = null;

    async function startStream() {
      try {
        setCameraError(null);
        if (stream) {
          stream.getTracks().forEach(t => t.stop());
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

        const constraints = {
          video: selectedDeviceId
            ? { deviceId: { exact: selectedDeviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } }
            : { facingMode: { ideal: "environment" }, width: { ideal: 1920 }, height: { ideal: 1080 } },
          audio: false
        };

        localStream = await navigator.mediaDevices.getUserMedia(constraints);
        if (!active) {
          localStream.getTracks().forEach(t => t.stop());
          return;
        }

        setStream(localStream);
        if (videoRef.current) {
          videoRef.current.srcObject = localStream;
        }
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
  }, [isOpen, selectedDeviceId]);

  // Clean up captured preview URLs on unmount/close
  useEffect(() => {
    return () => {
      capturedPages.forEach(p => URL.revokeObjectURL(p.previewUrl));
      if (currentSnapshot) URL.revokeObjectURL(currentSnapshot.previewUrl);
    };
  }, [capturedPages, currentSnapshot]);

  // Snap photo from live video stream
  const handleSnap = useCallback(async () => {
    if (!videoRef.current) return;
    const video = videoRef.current;

    const width = video.videoWidth || 1920;
    const height = video.videoHeight || 1080;

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.drawImage(video, 0, 0, width, height);

    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.95));
    if (!blob) return;

    const previewUrl = URL.createObjectURL(blob);
    setCurrentSnapshot({ blob, previewUrl, width, height });
    if (addLog) addLog(`📸 Captured frame (${width}×${height})`, "info");
  }, [addLog]);

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
      URL.revokeObjectURL(currentSnapshot.previewUrl);
      setCurrentSnapshot(null);
    }
  }, [currentSnapshot]);

  // Remove a page from batch
  const handleRemovePage = useCallback((indexToRemove) => {
    setCapturedPages(prev => {
      const target = prev[indexToRemove];
      if (target) URL.revokeObjectURL(target.previewUrl);
      return prev.filter((_, idx) => idx !== indexToRemove);
    });
  }, []);

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

  const selectedDevice = videoDevices.find(d => d.deviceId === selectedDeviceId);

  return (
    <div style={styles.overlay}>
      <div style={styles.modal}>
        {/* Header */}
        <div style={styles.header}>
          <div style={styles.headerTitleRow}>
            <span style={styles.headerIcon}>👓</span>
            <div>
              <h2 style={styles.title}>XREAL Eye Document Scanner</h2>
              <div style={styles.subtitle}>
                {selectedDevice ? (
                  <span style={styles.connectedBadge}>
                    🟢 {formatCameraLabel(selectedDevice, 0)}
                  </span>
                ) : videoDevices.length > 0 ? (
                  <span style={styles.connectedBadge}>
                    🟢 Connected ({videoDevices.length} {videoDevices.length === 1 ? "camera" : "cameras"})
                  </span>
                ) : (
                  <span style={styles.connectingBadge}>🟡 Initializing camera...</span>
                )}
              </div>
            </div>
          </div>

          <div style={styles.headerActions}>
            {videoDevices.length > 0 && (
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
            )}

            <button style={styles.closeBtn} onClick={onClose} disabled={processing}>
              ✕
            </button>
          </div>
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
                    <p style={{ margin: "0 0 10px" }}>Chrome blocks camera on plain <code style={styles.inlineCode}>http://</code> pages. Fix it on this device in 3 steps:</p>
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
          ) : currentSnapshot ? (
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
          ) : (
            <div style={styles.viewfinderContainer}>
              <video
                ref={videoRef}
                autoPlay
                playsInline
                muted
                style={styles.videoStream}
              />

              <div style={styles.alignmentGuide}>
                <div style={styles.cornerTL} />
                <div style={styles.cornerTR} />
                <div style={styles.cornerBL} />
                <div style={styles.cornerBR} />
                <div style={styles.alignmentPrompt}>
                  Align document page inside frame
                </div>
              </div>
            </div>
          )}

          {processing && (
            <div style={styles.processingOverlay}>
              <div style={styles.spinner} />
              <div style={styles.processingTitle}>Processing Document Batch</div>
              <div style={styles.processingStepText}>{processingStep}</div>
            </div>
          )}
        </div>

        {/* Multi-Page Tray (Thumbnails of added pages) */}
        {capturedPages.length > 0 && !processing && (
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
              >
                <div style={styles.shutterInner} />
              </button>
              <div style={styles.shutterLabel}>Tap to Capture Page</div>
            </div>
          ) : (
            <div style={styles.decisionControls}>
              <button
                style={styles.retakeBtn}
                onClick={handleRetake}
                disabled={processing}
              >
                🔄 Retake
              </button>

              <button
                style={styles.addPageBtn}
                onClick={handleAddPage}
                disabled={processing}
              >
                ➕ Add Page / Capture Another
              </button>

              <button
                style={styles.completeBtn}
                onClick={handleProcessBatch}
                disabled={processing}
              >
                ✨ Complete & Process ({capturedPages.length + 1} {capturedPages.length === 0 ? "Page" : "Pages"})
              </button>
            </div>
          )}

          {!currentSnapshot && capturedPages.length > 0 && !processing && (
            <button
              style={styles.floatingCompleteBtn}
              onClick={handleProcessBatch}
            >
              Finish & Process ({capturedPages.length} Pages) ➔
            </button>
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
    background: "rgba(0, 0, 0, 0.85)",
    backdropFilter: "blur(8px)",
    zIndex: 9999,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: "16px",
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
  },
  modal: {
    width: "100%",
    maxWidth: "680px",
    height: "92vh",
    maxHeight: "860px",
    background: "#121820",
    border: "1px solid #283e58",
    borderRadius: "18px",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    boxShadow: "0 20px 60px rgba(0,0,0,0.8)"
  },
  header: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    padding: "14px 20px",
    borderBottom: "1px solid #1f3044",
    background: "#182330"
  },
  headerTitleRow: {
    display: "flex",
    alignItems: "center",
    gap: "12px"
  },
  headerIcon: {
    fontSize: "1.6rem"
  },
  title: {
    margin: 0,
    fontSize: "1.1rem",
    fontWeight: 700,
    color: "#f0f4f8"
  },
  subtitle: {
    marginTop: "2px"
  },
  connectedBadge: {
    fontSize: "0.75rem",
    color: "#4ade80",
    fontWeight: 600
  },
  connectingBadge: {
    fontSize: "0.75rem",
    color: "#facc15"
  },
  headerActions: {
    display: "flex",
    alignItems: "center",
    gap: "10px"
  },
  cameraSelect: {
    background: "#0d141e",
    color: "#e2e8f0",
    border: "1px solid #2d4663",
    borderRadius: "8px",
    padding: "6px 10px",
    fontSize: "0.8rem",
    outline: "none",
    maxWidth: "180px"
  },
  closeBtn: {
    background: "transparent",
    border: "none",
    color: "#94a3b8",
    fontSize: "1.2rem",
    cursor: "pointer",
    padding: "6px 10px",
    borderRadius: "6px",
    transition: "background 0.2s"
  },
  body: {
    flex: 1,
    position: "relative",
    background: "#080c12",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden"
  },
  viewfinderContainer: {
    width: "100%",
    height: "100%",
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center"
  },
  videoStream: {
    width: "100%",
    height: "100%",
    objectFit: "contain"
  },
  alignmentGuide: {
    position: "absolute",
    width: "82%",
    height: "82%",
    border: "1px dashed rgba(56, 189, 248, 0.4)",
    borderRadius: "12px",
    pointerEvents: "none",
    display: "flex",
    alignItems: "flex-end",
    justifyContent: "center",
    paddingBottom: "16px"
  },
  cornerTL: {
    position: "absolute",
    top: "-2px",
    left: "-2px",
    width: "28px",
    height: "28px",
    borderTop: "4px solid #38bdf8",
    borderLeft: "4px solid #38bdf8",
    borderTopLeftRadius: "8px"
  },
  cornerTR: {
    position: "absolute",
    top: "-2px",
    right: "-2px",
    width: "28px",
    height: "28px",
    borderTop: "4px solid #38bdf8",
    borderRight: "4px solid #38bdf8",
    borderTopRightRadius: "8px"
  },
  cornerBL: {
    position: "absolute",
    bottom: "-2px",
    left: "-2px",
    width: "28px",
    height: "28px",
    borderBottom: "4px solid #38bdf8",
    borderLeft: "4px solid #38bdf8",
    borderBottomLeftRadius: "8px"
  },
  cornerBR: {
    position: "absolute",
    bottom: "-2px",
    right: "-2px",
    width: "28px",
    height: "28px",
    borderBottom: "4px solid #38bdf8",
    borderRight: "4px solid #38bdf8",
    borderBottomRightRadius: "8px"
  },
  alignmentPrompt: {
    background: "rgba(15, 23, 42, 0.75)",
    color: "#e2e8f0",
    padding: "6px 14px",
    borderRadius: "20px",
    fontSize: "0.8rem",
    letterSpacing: "0.5px"
  },
  previewContainer: {
    width: "100%",
    height: "100%",
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center"
  },
  previewImage: {
    maxWidth: "100%",
    maxHeight: "100%",
    objectFit: "contain",
    borderRadius: "6px"
  },
  previewBadge: {
    position: "absolute",
    top: "14px",
    left: "14px",
    background: "rgba(37, 99, 235, 0.85)",
    color: "#fff",
    padding: "6px 12px",
    borderRadius: "8px",
    fontSize: "0.8rem",
    fontWeight: 600
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
    zIndex: 10
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
    background: "#0f1722",
    borderTop: "1px solid #1e2c3c",
    padding: "10px 16px"
  },
  trayHeader: {
    fontSize: "0.75rem",
    color: "#94a3b8",
    marginBottom: "8px",
    textTransform: "uppercase",
    letterSpacing: "0.5px",
    fontWeight: 600
  },
  thumbnailsScroll: {
    display: "flex",
    gap: "10px",
    overflowX: "auto",
    paddingBottom: "4px"
  },
  thumbWrapper: {
    position: "relative",
    width: "60px",
    height: "80px",
    borderRadius: "6px",
    overflow: "hidden",
    border: "1px solid #334155",
    flexShrink: 0
  },
  thumbImg: {
    width: "100%",
    height: "100%",
    objectFit: "cover"
  },
  thumbIndex: {
    position: "absolute",
    bottom: 2,
    left: 2,
    background: "rgba(0,0,0,0.7)",
    color: "#fff",
    fontSize: "0.65rem",
    padding: "1px 4px",
    borderRadius: "3px"
  },
  thumbRemove: {
    position: "absolute",
    top: 2,
    right: 2,
    background: "rgba(239, 68, 68, 0.85)",
    color: "#fff",
    border: "none",
    width: "16px",
    height: "16px",
    borderRadius: "50%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    fontSize: "0.6rem",
    cursor: "pointer"
  },
  footer: {
    padding: "16px 20px",
    background: "#182330",
    borderTop: "1px solid #1f3044",
    display: "flex",
    flexDirection: "column",
    alignItems: "center"
  },
  controlRowCenter: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: "8px"
  },
  shutterBtn: {
    width: "72px",
    height: "72px",
    borderRadius: "50%",
    border: "4px solid #38bdf8",
    background: "transparent",
    cursor: "pointer",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    transition: "transform 0.15s"
  },
  shutterInner: {
    width: "56px",
    height: "56px",
    borderRadius: "50%",
    background: "#38bdf8"
  },
  shutterLabel: {
    color: "#94a3b8",
    fontSize: "0.8rem",
    fontWeight: 500
  },
  decisionControls: {
    display: "flex",
    gap: "10px",
    width: "100%",
    justifyContent: "center",
    flexWrap: "wrap"
  },
  retakeBtn: {
    padding: "10px 16px",
    borderRadius: "8px",
    border: "1px solid #475569",
    background: "#1e293b",
    color: "#cbd5e1",
    fontSize: "0.85rem",
    fontWeight: 600,
    cursor: "pointer"
  },
  addPageBtn: {
    padding: "10px 18px",
    borderRadius: "8px",
    border: "1px solid #2563eb",
    background: "#1d4ed8",
    color: "#ffffff",
    fontSize: "0.85rem",
    fontWeight: 600,
    cursor: "pointer"
  },
  completeBtn: {
    padding: "10px 22px",
    borderRadius: "8px",
    border: "none",
    background: "#10b981",
    color: "#ffffff",
    fontSize: "0.85rem",
    fontWeight: 700,
    cursor: "pointer"
  },
  floatingCompleteBtn: {
    marginTop: "10px",
    background: "transparent",
    border: "1px solid #10b981",
    color: "#10b981",
    padding: "6px 16px",
    borderRadius: "20px",
    fontSize: "0.8rem",
    fontWeight: 600,
    cursor: "pointer"
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