using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
using LyricsWidget.Models;

namespace LyricsWidget.Services
{
    public class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private bool _isDisposed;

        public event Action<TrackInfo>? TrackChanged;
        public event Action<TimeSpan, bool>? PlaybackStateChanged;

        private TrackInfo _currentTrack = new();
        private readonly Stopwatch _playbackStopwatch = new();
        private TimeSpan _baseTimelinePosition = TimeSpan.Zero;
        private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
        private bool _isPlaying = false;
        private DateTime _lastPollCheck = DateTime.UtcNow;

        public TrackInfo CurrentTrack => _currentTrack;

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    _sessionManager.SessionsChanged += OnSessionsChanged;
                    AttachToSession(GetBestSession());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to init GSMTC: {ex.Message}");
            }
        }

        public async Task ForceRescanSessionAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var best = GetBestSession();
                AttachToSession(best);
                if (best != null)
                {
                    await UpdateAllPropertiesAsync();
                }
                else
                {
                    _currentTrack = new TrackInfo();
                    _isPlaying = false;
                    _playbackStopwatch.Reset();
                    TrackChanged?.Invoke(_currentTrack);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ForceRescan error: {ex.Message}");
            }
        }

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            CheckAndSyncSession();
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            CheckAndSyncSession();
        }

        private GlobalSystemMediaTransportControlsSession? GetBestSession()
        {
            if (_sessionManager == null) return null;
            try
            {
                var sessions = _sessionManager.GetSessions();
                if (sessions != null && sessions.Count > 0)
                {
                    GlobalSystemMediaTransportControlsSession? playingSession = null;
                    GlobalSystemMediaTransportControlsSession? validSession = null;

                    foreach (var s in sessions)
                    {
                        try
                        {
                            var pb = s.GetPlaybackInfo();
                            bool isPlaying = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                            if (isPlaying && playingSession == null)
                            {
                                playingSession = s;
                            }
                            if (validSession == null)
                            {
                                validSession = s;
                            }
                        }
                        catch { }
                    }

                    if (playingSession != null) return playingSession;

                    var current = _sessionManager.GetCurrentSession();
                    if (current != null) return current;

                    return validSession ?? sessions[0];
                }

                return _sessionManager.GetCurrentSession();
            }
            catch
            {
                return _sessionManager?.GetCurrentSession();
            }
        }

        public void CheckAndSyncSession()
        {
            if (_sessionManager == null) return;
            try
            {
                var best = GetBestSession();
                if (best != null)
                {
                    if (_currentSession == null)
                    {
                        AttachToSession(best);
                        return;
                    }

                    bool isBestPlaying = false;
                    try
                    {
                        var info = best.GetPlaybackInfo();
                        isBestPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    }
                    catch { }

                    if (_currentSession != best && (isBestPlaying || !_isPlaying))
                    {
                        AttachToSession(best);
                        return;
                    }

                    var playback = _currentSession.GetPlaybackInfo();
                    if (playback != null)
                    {
                        bool isCurrentlyPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        if (isCurrentlyPlaying != _isPlaying)
                        {
                            UpdatePlayback();
                        }
                    }

                    var timeline = _currentSession.GetTimelineProperties();
                    if (timeline != null && timeline.LastUpdatedTime != _lastTimelineUpdate)
                    {
                        _lastTimelineUpdate = timeline.LastUpdatedTime;
                        _baseTimelinePosition = timeline.Position;
                        if (_isPlaying) _playbackStopwatch.Restart();
                    }

                    _ = CheckMediaPropertiesChangedAsync();
                }
                else if (_currentSession != null)
                {
                    AttachToSession(null);
                }
            }
            catch { }
        }

        private async Task CheckMediaPropertiesChangedAsync()
        {
            if (_currentSession == null) return;
            try
            {
                var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
                if (mediaProps != null)
                {
                    if (!string.Equals(mediaProps.Title, _currentTrack.Title, StringComparison.Ordinal) ||
                        !string.Equals(mediaProps.Artist, _currentTrack.Artist, StringComparison.Ordinal))
                    {
                        await UpdateAllPropertiesAsync();
                    }
                }
            }
            catch { }
        }

        private void AttachToSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _currentSession = session;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

                _ = UpdateAllPropertiesAsync();
            }
            else
            {
                _currentTrack = new TrackInfo();
                _isPlaying = false;
                _playbackStopwatch.Reset();
                TrackChanged?.Invoke(_currentTrack);
            }
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            UpdateTimeline();
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            UpdatePlayback();
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await UpdateAllPropertiesAsync();
        }

        public async Task UpdateAllPropertiesAsync()
        {
            if (_currentSession == null) return;

            try
            {
                var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
                var playbackInfo = _currentSession.GetPlaybackInfo();
                var timeline = _currentSession.GetTimelineProperties();

                byte[]? artBytes = null;
                if (mediaProps?.Thumbnail != null)
                {
                    artBytes = await ReadStreamToBytesAsync(mediaProps.Thumbnail);
                }

                _isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                _baseTimelinePosition = timeline?.Position ?? TimeSpan.Zero;
                _lastTimelineUpdate = timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow;

                _playbackStopwatch.Reset();
                if (_isPlaying)
                {
                    _playbackStopwatch.Start();
                }

                _currentTrack = new TrackInfo
                {
                    Title = mediaProps?.Title ?? string.Empty,
                    Artist = mediaProps?.Artist ?? string.Empty,
                    AlbumTitle = mediaProps?.AlbumTitle ?? string.Empty,
                    AlbumArtBytes = artBytes,
                    Duration = timeline?.EndTime ?? TimeSpan.Zero,
                    Position = _baseTimelinePosition,
                    IsPlaying = _isPlaying,
                    SourceApp = _currentSession.SourceAppUserModelId ?? "Media"
                };

                TrackChanged?.Invoke(_currentTrack);
                PlaybackStateChanged?.Invoke(GetEstimatedPosition(), _isPlaying);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading media properties: {ex.Message}");
            }
        }

        private void UpdateTimeline()
        {
            if (_currentSession == null) return;
            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null && timeline.LastUpdatedTime != _lastTimelineUpdate)
                {
                    _lastTimelineUpdate = timeline.LastUpdatedTime;
                    _baseTimelinePosition = timeline.Position;
                    if (_isPlaying)
                    {
                        _playbackStopwatch.Restart();
                    }
                    PlaybackStateChanged?.Invoke(GetEstimatedPosition(), _isPlaying);
                }
            }
            catch { }
        }

        private void UpdatePlayback()
        {
            if (_currentSession == null) return;
            try
            {
                var playback = _currentSession.GetPlaybackInfo();
                if (playback != null)
                {
                    bool wasPlaying = _isPlaying;
                    _isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    _currentTrack.IsPlaying = _isPlaying;

                    if (_isPlaying && !wasPlaying)
                    {
                        var timeline = _currentSession.GetTimelineProperties();
                        if (timeline != null && timeline.LastUpdatedTime != _lastTimelineUpdate)
                        {
                            _lastTimelineUpdate = timeline.LastUpdatedTime;
                            _baseTimelinePosition = timeline.Position;
                        }
                        _playbackStopwatch.Restart();
                    }
                    else if (!_isPlaying && wasPlaying)
                    {
                        _baseTimelinePosition += _playbackStopwatch.Elapsed;
                        _playbackStopwatch.Reset();
                    }

                    PlaybackStateChanged?.Invoke(GetEstimatedPosition(), _isPlaying);
                }
            }
            catch { }
        }

        public TimeSpan GetEstimatedPosition()
        {
            if ((DateTime.UtcNow - _lastPollCheck).TotalSeconds >= 1.0)
            {
                _lastPollCheck = DateTime.UtcNow;
                CheckAndSyncSession();
            }

            if (!_isPlaying)
            {
                return _baseTimelinePosition;
            }
            var estimated = _baseTimelinePosition + _playbackStopwatch.Elapsed;
            if (_currentTrack.Duration > TimeSpan.Zero && estimated > _currentTrack.Duration)
            {
                estimated = _currentTrack.Duration;
            }

            return estimated;
        }

        private static async Task<byte[]?> ReadStreamToBytesAsync(IRandomAccessStreamReference streamRef)
        {
            try
            {
                using var streamWithContent = await streamRef.OpenReadAsync();
                using var netStream = streamWithContent.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
            }
        }
    }
}
