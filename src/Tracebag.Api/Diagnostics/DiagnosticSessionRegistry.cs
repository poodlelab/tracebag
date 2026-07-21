using Tracebag.Api.Security;

namespace Tracebag.Api.Diagnostics;

public sealed class DiagnosticSessionRegistry
{
    private readonly Dictionary<string, DiagnosticSession> _sessionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sessionIdByTargetContainerId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public IDisposable ReserveTarget(string targetContainerId)
    {
        lock (_lock)
        {
            if (_sessionIdByTargetContainerId.ContainsKey(targetContainerId))
            {
                throw new TracebagException(
                    StatusCodes.Status409Conflict,
                    "counter_session_already_running",
                    "There is already an active counter session for this container.");
            }

            _sessionIdByTargetContainerId[targetContainerId] = string.Empty;
            return new TargetReservation(this, targetContainerId);
        }
    }

    public void Add(DiagnosticSession session)
    {
        lock (_lock)
        {
            if (_sessionIdByTargetContainerId.TryGetValue(session.TargetContainerId, out var existingSessionId)
                && !string.IsNullOrWhiteSpace(existingSessionId))
            {
                throw new TracebagException(
                    StatusCodes.Status409Conflict,
                    "counter_session_already_running",
                    "There is already an active counter session for this container.");
            }

            _sessionsById[session.SessionId] = session;
            _sessionIdByTargetContainerId[session.TargetContainerId] = session.SessionId;
        }
    }

    public DiagnosticSession Get(string sessionId)
    {
        lock (_lock)
        {
            return _sessionsById.TryGetValue(sessionId, out var session)
                ? session
                : throw new TracebagException(StatusCodes.Status404NotFound, "session_not_found", "The requested diagnostic session was not found.");
        }
    }

    public bool Remove(string sessionId, out DiagnosticSession? session)
    {
        lock (_lock)
        {
            if (!_sessionsById.Remove(sessionId, out session))
            {
                return false;
            }

            _sessionIdByTargetContainerId.Remove(session.TargetContainerId);
            return true;
        }
    }

    private void ReleaseReservation(string targetContainerId)
    {
        lock (_lock)
        {
            if (_sessionIdByTargetContainerId.TryGetValue(targetContainerId, out var sessionId)
                && string.IsNullOrWhiteSpace(sessionId))
            {
                _sessionIdByTargetContainerId.Remove(targetContainerId);
            }
        }
    }

    private sealed class TargetReservation : IDisposable
    {
        private readonly DiagnosticSessionRegistry _registry;
        private readonly string _targetContainerId;
        private bool _disposed;

        public TargetReservation(DiagnosticSessionRegistry registry, string targetContainerId)
        {
            _registry = registry;
            _targetContainerId = targetContainerId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _registry.ReleaseReservation(_targetContainerId);
            _disposed = true;
        }
    }
}
