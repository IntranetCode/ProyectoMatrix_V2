/* =====================================================
   ARCHIVO: wwwroot/js/universidad/video-player.js
   PROPÓSITO: Reproductor de video con tracking de progreso
   ===================================================== */

class UniversidadVideoPlayer {
    constructor() {
        this.video = null;
        this.isPlaying = false;
        this.currentTime = 0;
        this.duration = 0;
        this.watchedTime = 0;
        this.lastSaveTime = 0;
        this.saveInterval = 5000; // Guardar progreso cada 5 segundos
        this.progressThreshold = 0.95; // 95% para marcar como completado

        // Tracking data
        this.trackingData = {
            startTime: Date.now(),
            watchedSegments: [],
            totalWatchedTime: videoData.tiempoVistoPrevio || 0,
            lastPosition: 0,
            pauseCount: 0,
            seekCount: 0
        };

        // UI Elements
        this.elements = {
            video: null,
            playPauseBtn: null,
            progressBar: null,
            progressPlayed: null,
            progressBuffer: null,
            progressHandle: null,
            currentTimeDisplay: null,
            totalTimeDisplay: null,
            volumeBtn: null,
            volumeSlider: null,
            speedControl: null,
            fullscreenBtn: null,
            nextModuleBtn: null,
            startEvaluationBtn: null,
            overallProgress: null
        };

        this.isInitialized = false;
    }

    init() {
        if (this.isInitialized) return;

        this.setupElements();
        this.setupEventListeners();
        this.setupKeyboardControls();
        this.loadUserPreferences();
        this.loadPreviousProgress();
        this.startProgressTracking();

        this.isInitialized = true;
        console.log('🎥 Video Player Universidad inicializado');
    }

    setupElements() {
        this.video = document.getElementById('videoPlayer');
        this.elements = {
            video: this.video,
            playPauseBtn: document.getElementById('playPauseBtn'),
            progressBar: document.querySelector('.video-progress-bar'),
            progressPlayed: document.getElementById('progressPlayed'),
            progressBuffer: document.getElementById('progressBuffer'),
            progressHandle: document.getElementById('progressHandle'),
            currentTimeDisplay: document.getElementById('currentTime'),
            totalTimeDisplay: document.getElementById('totalTime'),
            volumeBtn: document.getElementById('volumeBtn'),
            volumeSlider: document.getElementById('volumeSlider'),
            speedControl: document.getElementById('playbackSpeed'),
            fullscreenBtn: document.getElementById('fullscreenBtn'),
            rewindBtn: document.getElementById('rewindBtn'),
            forwardBtn: document.getElementById('forwardBtn'),
            nextModuleBtn: document.getElementById('nextModuleBtn'),
            startEvaluationBtn: document.getElementById('startEvaluationBtn'),
            overallProgress: document.getElementById('overallProgress'),
            videoContainer: document.getElementById('videoContainer'),
            videoLoading: document.getElementById('videoLoading'),
            videoError: document.getElementById('videoError')
        };

        if (!this.video) {
            console.warn('⚠️ Elemento de video no encontrado');
            return;
        }
    }

    setupEventListeners() {
        if (!this.video) return;

        // Video Events
        this.video.addEventListener('loadedmetadata', () => this.onVideoLoaded());
        this.video.addEventListener('timeupdate', () => this.onTimeUpdate());
        this.video.addEventListener('play', () => this.onPlay());
        this.video.addEventListener('pause', () => this.onPause());
        this.video.addEventListener('ended', () => this.onVideoEnded());
        this.video.addEventListener('seeking', () => this.onSeeking());
        this.video.addEventListener('seeked', () => this.onSeeked());
        this.video.addEventListener('volumechange', () => this.onVolumeChange());
        this.video.addEventListener('error', () => this.onVideoError());
        this.video.addEventListener('waiting', () => this.showLoading());
        this.video.addEventListener('canplay', () => this.hideLoading());

        // Control Events
        if (this.elements.playPauseBtn) {
            this.elements.playPauseBtn.addEventListener('click', () => this.togglePlayPause());
        }

        if (this.elements.rewindBtn) {
            this.elements.rewindBtn.addEventListener('click', () => this.skipSeconds(-10));
        }

        if (this.elements.forwardBtn) {
            this.elements.forwardBtn.addEventListener('click', () => this.skipSeconds(10));
        }

        if (this.elements.volumeBtn) {
            this.elements.volumeBtn.addEventListener('click', () => this.toggleMute());
        }

        if (this.elements.volumeSlider) {
            this.elements.volumeSlider.addEventListener('input', (e) => this.setVolume(e.target.value / 100));
        }

        if (this.elements.speedControl) {
            this.elements.speedControl.addEventListener('change', (e) => this.setPlaybackSpeed(e.target.value));
        }

        if (this.elements.fullscreenBtn) {
            this.elements.fullscreenBtn.addEventListener('click', () => this.toggleFullscreen());
        }

        if (this.elements.progressBar) {
            this.elements.progressBar.addEventListener('click', (e) => this.seekToPosition(e));
            this.setupProgressBarDrag();
        }

        // Evaluation button
        if (this.elements.startEvaluationBtn) {
            this.elements.startEvaluationBtn.addEventListener('click', () => this.startEvaluation());
        }

        // Save notes
        const saveNotesBtn = document.getElementById('saveNotesBtn');
        if (saveNotesBtn) {
            saveNotesBtn.addEventListener('click', () => this.saveNotes());
        }

        // Window events
        window.addEventListener('beforeunload', () => this.saveProgress());
        document.addEventListener('visibilitychange', () => this.handleVisibilityChange());
    }

    setupKeyboardControls() {
        document.addEventListener('keydown', (e) => {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

            switch (e.code) {
                case 'Space':
                    e.preventDefault();
                    this.togglePlayPause();
                    break;
                case 'ArrowLeft':
                    e.preventDefault();
                    this.skipSeconds(-5);
                    break;
                case 'ArrowRight':
                    e.preventDefault();
                    this.skipSeconds(5);
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    this.adjustVolume(0.1);
                    break;
                case 'ArrowDown':
                    e.preventDefault();
                    this.adjustVolume(-0.1);
                    break;
                case 'KeyM':
                    e.preventDefault();
                    this.toggleMute();
                    break;
                case 'KeyF':
                    e.preventDefault();
                    this.toggleFullscreen();
                    break;
                case 'Digit1':
                case 'Digit2':
                case 'Digit3':
                case 'Digit4':
                case 'Digit5':
                case 'Digit6':
                case 'Digit7':
                case 'Digit8':
                case 'Digit9':
                    e.preventDefault();
                    const percent = parseInt(e.code.slice(-1)) / 10;
                    this.seekToPercent(percent);
                    break;
            }
        });
    }

    onVideoLoaded() {
        this.duration = this.video.duration;
        this.hideLoading();

        if (this.elements.totalTimeDisplay) {
            this.elements.totalTimeDisplay.textContent = this.formatTime(this.duration);
        }

        // Restaurar posición previa si existe
        const savedPosition = localStorage.getItem(`video_position_${videoData.subCursoId}`);
        if (savedPosition && parseFloat(savedPosition) > 0) {
            this.video.currentTime = parseFloat(savedPosition);
        }

        this.updateProgress();
        console.log(`📹 Video cargado: ${this.formatTime(this.duration)}`);
    }

    onTimeUpdate() {
        this.currentTime = this.video.currentTime;
        this.updateWatchedTime();
        this.updateProgressBar();
        this.updateTimeDisplay();
        this.checkCompletionStatus();

        // Guardar progreso periódicamente
        if (Date.now() - this.lastSaveTime > this.saveInterval) {
            this.saveProgress();
            this.lastSaveTime = Date.now();
        }
    }

    onPlay() {
        this.isPlaying = true;
        this.trackingData.lastPosition = this.currentTime;

        if (this.elements.playPauseBtn) {
            this.elements.playPauseBtn.innerHTML = '<i class="fas fa-pause"></i>';
        }

        console.log('▶️ Video iniciado en:', this.formatTime(this.currentTime));
    }

    onPause() {
        this.isPlaying = false;
        this.trackingData.pauseCount++;

        if (this.elements.playPauseBtn) {
            this.elements.playPauseBtn.innerHTML = '<i class="fas fa-play"></i>';
        }

        this.saveProgress();
        console.log('⏸️ Video pausado en:', this.formatTime(this.currentTime));
    }

    onVideoEnded() {
        this.isPlaying = false;
        this.markAsCompleted();

        if (this.elements.playPauseBtn) {
            this.elements.playPauseBtn.innerHTML = '<i class="fas fa-replay"></i>';
        }

        this.saveProgress();
        this.showCompletionMessage();
        console.log('✅ Video completado');
    }

    onSeeking() {
        this.trackingData.seekCount++;
        this.showLoading();
    }

    onSeeked() {
        this.hideLoading();
        this.trackingData.lastPosition = this.currentTime;
    }

    onVolumeChange() {
        const volume = this.video.volume;
        const isMuted = this.video.muted;

        if (this.elements.volumeSlider) {
            this.elements.volumeSlider.value = volume * 100;
        }

        if (this.elements.volumeBtn) {
            const icon = this.elements.volumeBtn.querySelector('i');
            if (isMuted || volume === 0) {
                if (isMuted || volume === 0) {
                    icon.className = 'fas fa-volume-mute';
                } else if (volume < 0.5) {
                    icon.className = 'fas fa-volume-down';
                } else {
                    icon.className = 'fas fa-volume-up';
                }
            }

            // Guardar preferencia de volumen
            localStorage.setItem('video_volume', volume);
        }

        onVideoError() {
            console.error('❌ Error al cargar el video');
            this.hideLoading();
            if (this.elements.videoError) {
                this.elements.videoError.style.display = 'flex';
            }
        }

        // ===== CONTROL METHODS =====

        togglePlayPause() {
            if (!this.video) return;

            if (this.video.paused) {
                this.video.play().catch(e => {
                    console.error('Error al reproducir video:', e);
                    this.showNotification('Error al reproducir el video', 'error');
                });
            } else {
                this.video.pause();
            }
        }

        skipSeconds(seconds) {
            if (!this.video) return;

            const newTime = Math.max(0, Math.min(this.video.currentTime + seconds, this.duration));
            this.video.currentTime = newTime;

            this.showSkipIndicator(seconds);
        }

        seekToPercent(percent) {
            if (!this.video || !this.duration) return;

            const newTime = this.duration * percent;
            this.video.currentTime = newTime;
        }

        seekToPosition(event) {
            if (!this.video || !this.duration) return;

            const rect = this.elements.progressBar.getBoundingClientRect();
            const percent = (event.clientX - rect.left) / rect.width;
            const newTime = this.duration * percent;

            this.video.currentTime = Math.max(0, Math.min(newTime, this.duration));
        }

        setupProgressBarDrag() {
            let isDragging = false;
            let wasPlaying = false;

            const startDrag = (e) => {
                isDragging = true;
                wasPlaying = !this.video.paused;
                if (wasPlaying) this.video.pause();

                document.addEventListener('mousemove', onDrag);
                document.addEventListener('mouseup', stopDrag);
            };

            const onDrag = (e) => {
                if (!isDragging) return;

                const rect = this.elements.progressBar.getBoundingClientRect();
                const percent = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                const newTime = this.duration * percent;

                this.video.currentTime = newTime;
                this.updateProgressBar();
            };

            const stopDrag = () => {
                isDragging = false;
                if (wasPlaying) this.video.play();

                document.removeEventListener('mousemove', onDrag);
                document.removeEventListener('mouseup', stopDrag);
            };

            if (this.elements.progressHandle) {
                this.elements.progressHandle.addEventListener('mousedown', startDrag);
            }
            if (this.elements.progressBar) {
                this.elements.progressBar.addEventListener('mousedown', startDrag);
            }
        }

        toggleMute() {
            if (!this.video) return;

            this.video.muted = !this.video.muted;
        }

        setVolume(volume) {
            if (!this.video) return;

            this.video.volume = Math.max(0, Math.min(1, volume));
            this.video.muted = false;
        }

        adjustVolume(delta) {
            const newVolume = this.video.volume + delta;
            this.setVolume(newVolume);
        }

        setPlaybackSpeed(speed) {
            if (!this.video) return;

            this.video.playbackRate = parseFloat(speed);
            localStorage.setItem('video_playback_speed', speed);
        }

        toggleFullscreen() {
            const container = this.elements.videoContainer;

            if (!document.fullscreenElement) {
                container.requestFullscreen().catch(err => {
                    console.error('Error al entrar en pantalla completa:', err);
                });
            } else {
                document.exitFullscreen();
            }
        }

        // ===== PROGRESS TRACKING =====

        updateWatchedTime() {
            if (this.isPlaying) {
                const timeDiff = this.currentTime - this.trackingData.lastPosition;
                if (timeDiff > 0 && timeDiff < 2) { // Solo contar tiempo lineal
                    this.trackingData.totalWatchedTime += timeDiff;
                }
                this.trackingData.lastPosition = this.currentTime;
            }
        }

        updateProgressBar() {
            if (!this.duration) return;

            const percent = (this.currentTime / this.duration) * 100;

            if (this.elements.progressPlayed) {
                this.elements.progressPlayed.style.width = `${percent}%`;
            }

            if (this.elements.progressHandle) {
                this.elements.progressHandle.style.left = `${percent}%`;
            }

            // Update buffer
            if (this.video.buffered.length > 0) {
                const bufferPercent = (this.video.buffered.end(this.video.buffered.length - 1) / this.duration) * 100;
                if (this.elements.progressBuffer) {
                    this.elements.progressBuffer.style.width = `${bufferPercent}%`;
                }
            }
        }

        updateTimeDisplay() {
            if (this.elements.currentTimeDisplay) {
                this.elements.currentTimeDisplay.textContent = this.formatTime(this.currentTime);
            }

            // Update watched time in sidebar
            const timeWatchedElement = document.getElementById('timeWatched');
            if (timeWatchedElement) {
                timeWatchedElement.textContent = this.formatTime(this.trackingData.totalWatchedTime);
            }
        }

        updateProgress() {
            if (!this.duration) return;

            const watchedPercent = Math.min(100, (this.trackingData.totalWatchedTime / this.duration) * 100);

            if (this.elements.overallProgress) {
                this.elements.overallProgress.style.width = `${watchedPercent}%`;
            }

            // Update progress percentage in UI
            const progressElements = document.querySelectorAll('.stat-value');
            progressElements.forEach(element => {
                if (element.textContent.includes('%')) {
                    element.textContent = `${watchedPercent.toFixed(1)}%`;
                }
            });
        }

        checkCompletionStatus() {
            if (!this.duration) return;

            const watchedPercent = (this.trackingData.totalWatchedTime / this.duration);
            const isCompleted = watchedPercent >= this.progressThreshold;

            // Habilitar botón de evaluación si está disponible
            if (videoData.requiereEvaluacion && isCompleted && this.elements.startEvaluationBtn) {
                this.elements.startEvaluationBtn.disabled = false;
                this.elements.startEvaluationBtn.classList.remove('btn-secondary');
                this.elements.startEvaluationBtn.classList.add('btn-warning');
            }

            // Habilitar botón siguiente módulo si está completado
            if (isCompleted && this.elements.nextModuleBtn) {
                this.elements.nextModuleBtn.disabled = false;
            }

            if (isCompleted && !videoData.completado) {
                this.markAsCompleted();
            }
        }
    
    async markAsCompleted() {
            try {
                const response = await fetch('/Universidad/ActualizarProgreso', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        subCursoID: videoData.subCursoId,
                        tiempoTotalVisto: Math.floor(this.trackingData.totalWatchedTime),
                        porcentajeVisto: Math.min(100, (this.trackingData.totalWatchedTime / this.duration) * 100)
                    })
                });

                if (response.ok) {
                    videoData.completado = true;
                    this.showNotification('¡Módulo completado exitosamente!', 'success');
                    this.updateCompletionUI();
                }
            } catch (error) {
                console.error('Error al marcar como completado:', error);
            }
        }
    
    async saveProgress() {
            try {
                const progressData = {
                    subCursoID: videoData.subCursoId,
                    tiempoTotalVisto: Math.floor(this.trackingData.totalWatchedTime),
                    porcentajeVisto: Math.min(100, (this.trackingData.totalWatchedTime / this.duration) * 100)
                };

                await fetch('/Universidad/ActualizarProgreso', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify(progressData)
                });

                // Guardar posición local
                localStorage.setItem(`video_position_${videoData.subCursoId}`, this.currentTime);

                console.log('💾 Progreso guardado:', progressData);
            } catch (error) {
                console.error('Error al guardar progreso:', error);
            }
        }

        // ===== UI METHODS =====

        showLoading() {
            if (this.elements.videoLoading) {
                this.elements.videoLoading.style.display = 'flex';
            }
        }

        hideLoading() {
            if (this.elements.videoLoading) {
                this.elements.videoLoading.style.display = 'none';
            }
        }

        showSkipIndicator(seconds) {
            const indicator = document.createElement('div');
            indicator.className = 'skip-indicator';
            indicator.innerHTML = `<i class="fas fa-${seconds > 0 ? 'forward' : 'backward'}"></i> ${Math.abs(seconds)}s`;

            this.elements.videoContainer.appendChild(indicator);

            setTimeout(() => {
                indicator.remove();
            }, 1000);
        }

        showCompletionMessage() {
            const message = document.createElement('div');
            message.className = 'completion-message';
            message.innerHTML = `
            <div class="completion-content">
                <i class="fas fa-check-circle fa-3x text-success mb-3"></i>
                <h5>¡Módulo Completado!</h5>
                <p>Has completado exitosamente este módulo.</p>
                ${videoData.requiereEvaluacion ? '<button class="btn btn-warning mt-2" onclick="startEvaluation()">Tomar Evaluación</button>' : ''}
            </div>
        `;

            this.elements.videoContainer.appendChild(message);

            setTimeout(() => {
                message.remove();
            }, 5000);
        }

        showNotification(message, type = 'info') {
            if (window.UniversidadLayout && window.UniversidadLayout.showToast) {
                window.UniversidadLayout.showToast(message, type);
            } else {
                console.log(`📢 ${type.toUpperCase()}: ${message}`);
            }
        }

        updateCompletionUI() {
            // Update status badges
            const statusBadges = document.querySelectorAll('.badge');
            statusBadges.forEach(badge => {
                if (badge.textContent.includes('Progreso') || badge.textContent.includes('Disponible')) {
                    badge.className = 'badge bg-success';
                    badge.textContent = 'Completado';
                }
            });

            // Update progress bars
            if (this.elements.overallProgress) {
                this.elements.overallProgress.style.width = '100%';
                this.elements.overallProgress.classList.remove('bg-primary');
                this.elements.overallProgress.classList.add('bg-success');
            }
        }

    // ===== EVALUATION METHODS =====
    
    async startEvaluation() {
            if (!videoData.requiereEvaluacion) return;

            try {
                const response = await fetch(`/Universidad/Api/Evaluacion/${videoData.subCursoId}`);
                if (response.ok) {
                    const evaluationData = await response.json();
                    this.showEvaluationModal(evaluationData);
                }
            } catch (error) {
                console.error('Error al cargar evaluación:', error);
                this.showNotification('Error al cargar la evaluación', 'error');
            }
        }

        showEvaluationModal(data) {
            const modal = document.getElementById('evaluationModal');
            const content = document.getElementById('evaluationContent');

            content.innerHTML = `
            <div class="evaluation-intro">
                <p><strong>Instrucciones:</strong> Responde todas las preguntas. Puntaje mínimo: ${videoData.puntajeMinimo}%</p>
                <hr>
            </div>
            <div class="evaluation-questions">
                <!-- Las preguntas se cargarían aquí dinámicamente -->
                <p class="text-muted">Sistema de evaluación en desarrollo...</p>
            </div>
        `;

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        }

    // ===== NOTES METHODS =====
    
    async saveNotes() {
            const notesTextarea = document.getElementById('videoNotes');
            if (!notesTextarea) return;

            const notes = notesTextarea.value.trim();

            try {
                const response = await fetch('/Universidad/Api/GuardarNotas', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        subCursoId: videoData.subCursoId,
                        notas: notes
                    })
                });

                if (response.ok) {
                    this.showNotification('Notas guardadas correctamente', 'success');
                }
            } catch (error) {
                console.error('Error al guardar notas:', error);
                this.showNotification('Error al guardar las notas', 'error');
            }
        }

        // ===== UTILITY METHODS =====

        formatTime(seconds) {
            const hours = Math.floor(seconds / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            const secs = Math.floor(seconds % 60);

            if (hours > 0) {
                return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
            }
            return `${minutes}:${secs.toString().padStart(2, '0')}`;
        }

        loadUserPreferences() {
            // Volumen guardado
            const savedVolume = localStorage.getItem('video_volume');
            if (savedVolume && this.video) {
                this.video.volume = parseFloat(savedVolume);
            }

            // Velocidad de reproducción
            const savedSpeed = localStorage.getItem('video_playback_speed');
            if (savedSpeed && this.elements.speedControl) {
                this.elements.speedControl.value = savedSpeed;
                if (this.video) {
                    this.video.playbackRate = parseFloat(savedSpeed);
                }
            }
        }

        loadPreviousProgress() {
            // Cargar tiempo visto previamente
            this.trackingData.totalWatchedTime = videoData.tiempoVistoPrevio || 0;
            this.updateProgress();
        }

        startProgressTracking() {
            // Iniciar tracking automático cada segundo
            setInterval(() => {
                if (this.isPlaying) {
                    this.updateWatchedTime();
                    this.updateProgress();
                }
            }, 1000);

            // Auto-save cada 30 segundos
            setInterval(() => {
                if (this.trackingData.totalWatchedTime > 0) {
                    this.saveProgress();
                }
            }, 30000);
        }

        handleVisibilityChange() {
            if (document.visibilityState === 'hidden') {
                // Guardar progreso cuando se oculta la pestaña
                this.saveProgress();
            }
        }

        // ===== PUBLIC API =====

        getCurrentTime() {
            return this.currentTime;
        }

        getDuration() {
            return this.duration;
        }

        getWatchedTime() {
            return this.trackingData.totalWatchedTime;
        }

        getWatchedPercent() {
            return this.duration > 0 ? (this.trackingData.totalWatchedTime / this.duration) * 100 : 0;
        }

        isVideoCompleted() {
            return this.getWatchedPercent() >= (this.progressThreshold * 100);
        }
    }

// ===== GLOBAL FUNCTIONS =====

function initVideoPlayer() {
    window.universidadVideoPlayer = new UniversidadVideoPlayer();
    window.universidadVideoPlayer.init();
}

function startEvaluation() {
    if (window.universidadVideoPlayer) {
        window.universidadVideoPlayer.startEvaluation();
    }
}

// ===== CSS STYLES FOR VIDEO PLAYER =====
const videoPlayerStyles = `
<style>
.video-player-container {
    background: var(--gray-50);
    min-height: 100vh;
}

.video-header {
    background: white;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    padding: 1.5rem 0;
}

.video-title {
    color: var(--universidad-primary);
    font-weight: 700;
    font-size: 1.5rem;
    margin-bottom: 0.5rem;
}

.video-meta {
    display: flex;
    gap: 1.5rem;
    flex-wrap: wrap;
}

.meta-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--gray-600);
    font-size: 0.9rem;
}

.video-container {
    position: relative;
    background: #000;
    border-radius: 12px;
    overflow: hidden;
    aspect-ratio: 16/9;
    margin-bottom: 1.5rem;
}

.video-player {
    width: 100%;
    height: 100%;
    object-fit: contain;
}

.video-controls-overlay {
    position: absolute;
    bottom: 0;
    left: 0;
    right: 0;
    background: linear-gradient(transparent, rgba(0,0,0,0.8));
    color: white;
    padding: 1rem;
    opacity: 0;
    transition: opacity 0.3s ease;
}

.video-container:hover .video-controls-overlay {
    opacity: 1;
}

.video-progress-container {
    margin-bottom: 1rem;
}

.video-progress-bar {
    position: relative;
    height: 6px;
    background: rgba(255,255,255,0.3);
    border-radius: 3px;
    cursor: pointer;
    margin-bottom: 0.5rem;
}

.progress-buffer {
    position: absolute;
    height: 100%;
    background: rgba(255,255,255,0.5);
    border-radius: 3px;
    width: 0%;
    transition: width 0.1s ease;
}

.progress-played {
    position: absolute;
    height: 100%;
    background: var(--universidad-primary);
    border-radius: 3px;
    width: 0%;
    transition: width 0.1s ease;
}

.progress-handle {
    position: absolute;
    width: 12px;
    height: 12px;
    background: white;
    border-radius: 50%;
    top: -3px;
    left: 0%;
    cursor: pointer;
    opacity: 0;
    transition: opacity 0.3s ease;
}

.video-progress-bar:hover .progress-handle {
    opacity: 1;
}

.progress-time {
    display: flex;
    justify-content: space-between;
    font-size: 0.9rem;
}

.video-controls {
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.controls-left,
.controls-right {
    display: flex;
    align-items: center;
    gap: 0.5rem;
}

.control-btn {
    background: none;
    border: none;
    color: white;
    padding: 0.5rem;
    border-radius: 4px;
    cursor: pointer;
    transition: background 0.2s ease;
    display: flex;
    align-items: center;
    gap: 0.25rem;
}

.control-btn:hover {
    background: rgba(255,255,255,0.2);
}

.volume-control {
    display: flex;
    align-items: center;
    gap: 0.5rem;
}

.volume-slider {
    width: 80px;
    accent-color: var(--universidad-primary);
}

.video-loading,
.video-error {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(0,0,0,0.8);
    color: white;
    flex-direction: column;
    gap: 1rem;
}

.loading-spinner {
    width: 40px;
    height: 40px;
    border: 3px solid rgba(255,255,255,0.3);
    border-top: 3px solid white;
    border-radius: 50%;
    animation: spin 1s linear infinite;
}

.no-video-placeholder {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--gray-500);
}

.skip-indicator {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    background: rgba(0,0,0,0.8);
    color: white;
    padding: 1rem;
    border-radius: 8px;
    font-size: 1.1rem;
    animation: fadeInOut 1s ease;
}

.completion-message {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(0,0,0,0.9);
    color: white;
    animation: fadeIn 0.5s ease;
}

.completion-content {
    text-align: center;
    padding: 2rem;
}

@keyframes spin {
    0% { transform: rotate(0deg); }
    100% { transform: rotate(360deg); }
}

@keyframes fadeInOut {
    0%, 100% { opacity: 0; transform: translate(-50%, -50%) scale(0.8); }
    50% { opacity: 1; transform: translate(-50%, -50%) scale(1); }
}

@keyframes fadeIn {
    from { opacity: 0; }
    to { opacity: 1; }
}

/* Sidebar styles */
.navigation-controls,
.additional-materials,
.evaluation-section,
.video-notes {
    background: white;
    border-radius: 12px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    overflow: hidden;
}

.nav-header,
.materials-header,
.evaluation-header,
.notes-header {
    padding: 1rem;
    background: var(--gray-50);
    border-bottom: 1px solid var(--gray-200);
}

.nav-buttons,
.materials-content,
.evaluation-content,
.notes-content {
    padding: 1rem;
}

.material-item {
    display: flex;
    align-items: center;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid var(--gray-200);
    border-radius: 8px;
}

.material-icon {
    font-size: 1.5rem;
}

.material-info {
    flex-grow: 1;
}

.material-info h6 {
    margin: 0 0 0.25rem 0;
    font-size: 0.9rem;
}

.material-info p {
    margin: 0;
    font-size: 0.8rem;
}

.material-actions {
    display: flex;
    gap: 0.5rem;
}

.progress-lg {
    height: 12px;
}

@media (max-width: 768px) {
    .video-meta {
        gap: 1rem;
    }
    
    .video-actions {
        margin-top: 1rem;
        text-align: left !important;
    }
    
    .controls-left,
    .controls-right {
        gap: 0.25rem;
    }
    
    .volume-slider {
        width: 60px;
    }
}
</style>
`;

// Inyectar estilos
document.head.insertAdjacentHTML('beforeend', videoPlayerStyles);

// Debug helpers
if (window.location.hostname === 'localhost') {
    window.debugVideoPlayer = {
        player: () => window.universidadVideoPlayer,
        data: videoData,

        simulateCompletion() {
            if (window.universidadVideoPlayer) {
                window.universidadVideoPlayer.trackingData.totalWatchedTime = window.universidadVideoPlayer.duration * 0.96;
                window.universidadVideoPlayer.updateProgress();
                window.universidadVideoPlayer.checkCompletionStatus();
            }
        },

        logProgress() {
            if (window.universidadVideoPlayer) {
                console.log('Video Progress:', {
                    currentTime: window.universidadVideoPlayer.getCurrentTime(),
                    duration: window.universidadVideoPlayer.getDuration(),
                    watchedTime: window.universidadVideoPlayer.getWatchedTime(),
                    watchedPercent: window.universidadVideoPlayer.getWatchedPercent(),
                    isCompleted: window.universidadVideoPlayer.isVideoCompleted()
                });
            }
        }
    };
}