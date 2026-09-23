namespace AiRepoKit.Agents.Runtime;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using AiRepoKit.Agents;

public sealed class ResilientModelExecutionRuntime :
    IResumableModelSessionRuntime
{
    private readonly IModelRoutingRuntime _routingRuntime;
    private readonly AgentCapabilitySet _requiredCapabilities;
    private readonly ModelRuntimeResiliencePolicy _resiliencePolicy;
    private readonly IModelExecutionFailureClassifier _failureClassifier;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public ResilientModelExecutionRuntime(
        IModelRoutingRuntime routingRuntime_,
        AgentCapabilitySet requiredCapabilities_,
        ModelRuntimeResiliencePolicy resiliencePolicy_,
        IModelExecutionFailureClassifier failureClassifier_)
        : this(
            routingRuntime_,
            requiredCapabilities_,
            resiliencePolicy_,
            failureClassifier_,
            TimeProvider.System)
    {
    }

    internal ResilientModelExecutionRuntime(
        IModelRoutingRuntime routingRuntime_,
        AgentCapabilitySet requiredCapabilities_,
        ModelRuntimeResiliencePolicy resiliencePolicy_,
        IModelExecutionFailureClassifier failureClassifier_,
        TimeProvider timeProvider_)
    {
        ArgumentNullException.ThrowIfNull(
            routingRuntime_,
            nameof(routingRuntime_));

        ArgumentNullException.ThrowIfNull(
            requiredCapabilities_,
            nameof(requiredCapabilities_));

        ArgumentNullException.ThrowIfNull(
            resiliencePolicy_,
            nameof(resiliencePolicy_));

        ArgumentNullException.ThrowIfNull(
            failureClassifier_,
            nameof(failureClassifier_));

        ArgumentNullException.ThrowIfNull(
            timeProvider_,
            nameof(timeProvider_));

        this._routingRuntime =
            routingRuntime_;
        this._requiredCapabilities =
            requiredCapabilities_;
        this._resiliencePolicy =
            resiliencePolicy_;
        this._failureClassifier =
            failureClassifier_;
        this._timeProvider =
            timeProvider_;
    }

    public async Task<ModelExecutionResult> ExecuteAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        IReadOnlyList<ModelRouteCandidate> route =
            await AcquireRouteAsync(cancellationToken_).ConfigureAwait(false);

        for (int candidateIndex = 0; candidateIndex < route.Count; candidateIndex++)
        {
            ModelRouteCandidate candidate = route[candidateIndex];
            IModelExecutionRuntime candidateRuntime = candidate.Registration.Runtime;

            for (int attempt = 1; attempt <= this._resiliencePolicy.MaxAttemptsPerCandidate; attempt++)
            {
                cancellationToken_.ThrowIfCancellationRequested();

                ModelExecutionResult result;
                try
                {
                    result = await candidateRuntime
                        .ExecuteAsync(request_, cancellationToken_)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cancellationToken_.ThrowIfCancellationRequested();

                    bool isRetryable = this._failureClassifier.IsRetryable(candidate, ex);
                    if (!isRetryable)
                    {
                        throw;
                    }

                    if (attempt < this._resiliencePolicy.MaxAttemptsPerCandidate)
                    {
                        if (this._resiliencePolicy.RetryBackoff > TimeSpan.Zero)
                        {
                            await Task.Delay(
                                this._resiliencePolicy.RetryBackoff,
                                this._timeProvider,
                                cancellationToken_).ConfigureAwait(false);
                        }

                        continue;
                    }
                    else
                    {
                        if (candidateIndex == route.Count - 1)
                        {
                            throw;
                        }

                        break;
                    }
                }

                if (result is null)
                {
                    throw new InvalidOperationException("The candidate runtime returned a null execution result.");
                }

                return result;
            }
        }

        throw new InvalidOperationException("Execution failed without a terminal exception.");
    }

    public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        return ExecuteStreamingCoreAsync(request_, cancellationToken_);
    }

    public AgentSessionReference CreateSession()
    {
        string sessionId = Guid.NewGuid().ToString("N");
        AgentSessionReference sessionRef = new(sessionId);
        SessionState state = new(sessionRef, this._sessions);
        this._sessions[sessionId] = state;
        return sessionRef;
    }

    public async Task<ModelExecutionResult> ExecuteInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? state))
        {
            throw new InvalidOperationException("Unknown session reference.");
        }

        if (!state.TryAcquireTurn())
        {
            throw new InvalidOperationException("A turn is already in progress for this session.");
        }

        try
        {
            if (state.IsEnded)
            {
                throw new InvalidOperationException("The session has ended.");
            }

            await EnsureBoundAsync(state, cancellationToken_).ConfigureAwait(false);

            ModelRouteCandidate candidate = state.BoundCandidate!;
            IResumableModelSessionRuntime candidateRuntime = state.BoundRuntime!;
            AgentSessionReference underlyingSession = state.UnderlyingSession!;

            for (int attempt = 1; attempt <= this._resiliencePolicy.MaxAttemptsPerCandidate; attempt++)
            {
                cancellationToken_.ThrowIfCancellationRequested();

                ModelExecutionResult result;
                try
                {
                    result = await candidateRuntime
                        .ExecuteInSessionAsync(underlyingSession, request_, cancellationToken_)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cancellationToken_.ThrowIfCancellationRequested();

                    bool isRetryable = this._failureClassifier.IsRetryable(candidate, ex);
                    if (!isRetryable)
                    {
                        throw;
                    }

                    if (attempt < this._resiliencePolicy.MaxAttemptsPerCandidate)
                    {
                        if (this._resiliencePolicy.RetryBackoff > TimeSpan.Zero)
                        {
                            await Task.Delay(
                                this._resiliencePolicy.RetryBackoff,
                                this._timeProvider,
                                cancellationToken_).ConfigureAwait(false);
                        }

                        continue;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (result is null)
                {
                    throw new InvalidOperationException("The candidate runtime returned a null execution result.");
                }

                return result;
            }

            throw new InvalidOperationException("Session execution failed without a terminal exception.");
        }
        finally
        {
            state.ReleaseTurn();
        }
    }

    public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? state))
        {
            throw new InvalidOperationException("Unknown session reference.");
        }

        return ExecuteStreamingInSessionCoreAsync(state, request_, cancellationToken_);
    }

    public async Task<bool> EndSessionAsync(
        AgentSessionReference sessionReference_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        cancellationToken_.ThrowIfCancellationRequested();

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? state))
        {
            return false;
        }

        return await state.EndAsync(cancellationToken_).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ModelRouteCandidate>> AcquireRouteAsync(
        CancellationToken cancellationToken_)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        IReadOnlyList<ModelRouteCandidate> route = await this._routingRuntime
            .RouteAsync(this._requiredCapabilities, cancellationToken_)
            .ConfigureAwait(false);

        if (route is null)
        {
            throw new InvalidOperationException("The routing runtime returned a null route.");
        }

        if (route.Count == 0)
        {
            throw new InvalidOperationException("The routing runtime returned an empty route.");
        }

        for (int i = 0; i < route.Count; i++)
        {
            ModelRouteCandidate candidate = route[i];
            if (candidate is null ||
                candidate.Registration is null ||
                candidate.Registration.Runtime is null)
            {
                throw new InvalidOperationException($"The routing runtime returned an invalid or null candidate at index {i}.");
            }
        }

        return route;
    }

    private async IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingCoreAsync(
        ModelExecutionRequest request_,
        CancellationToken methodToken_,
        [EnumeratorCancellation] CancellationToken enumeratorToken_ = default)
    {
        using CancellationTokenSource? linkedCts =
            (methodToken_.CanBeCanceled && enumeratorToken_.CanBeCanceled && methodToken_ != enumeratorToken_)
                ? CancellationTokenSource.CreateLinkedTokenSource(methodToken_, enumeratorToken_)
                : null;

        CancellationToken callerToken = linkedCts is not null
            ? linkedCts.Token
            : (methodToken_.CanBeCanceled ? methodToken_ : enumeratorToken_);

        callerToken.ThrowIfCancellationRequested();

        IReadOnlyList<ModelRouteCandidate> route =
            await AcquireRouteAsync(callerToken).ConfigureAwait(false);

        List<(ModelRouteCandidate Candidate, IModelStreamingExecutionRuntime Runtime)> eligible = [];
        for (int i = 0; i < route.Count; i++)
        {
            if (route[i].Registration.Runtime is IModelStreamingExecutionRuntime streamingRuntime)
            {
                eligible.Add((route[i], streamingRuntime));
            }
        }

        if (eligible.Count == 0)
        {
            throw new InvalidOperationException("No candidates in the route support streaming.");
        }

        for (int candidateIndex = 0; candidateIndex < eligible.Count; candidateIndex++)
        {
            var (candidate, candidateRuntime) = eligible[candidateIndex];

            for (int attempt = 1; attempt <= this._resiliencePolicy.MaxAttemptsPerCandidate; attempt++)
            {
                callerToken.ThrowIfCancellationRequested();

                IAsyncEnumerable<ModelExecutionUpdate> stream;
                try
                {
                    stream = candidateRuntime.ExecuteStreamingAsync(request_, callerToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    bool shouldRetrySameCandidate = await HandleCandidateFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        candidateIndex,
                        attempt,
                        eligible.Count,
                        callerToken).ConfigureAwait(false);

                    if (shouldRetrySameCandidate)
                    {
                        continue;
                    }

                    break;
                }

                if (stream is null)
                {
                    throw new InvalidOperationException("The streaming candidate returned a null IAsyncEnumerable.");
                }

                IAsyncEnumerator<ModelExecutionUpdate> enumerator;
                try
                {
                    enumerator = stream.GetAsyncEnumerator(callerToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    bool shouldRetrySameCandidate = await HandleCandidateFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        candidateIndex,
                        attempt,
                        eligible.Count,
                        callerToken).ConfigureAwait(false);

                    if (shouldRetrySameCandidate)
                    {
                        continue;
                    }

                    break;
                }

                if (enumerator is null)
                {
                    throw new InvalidOperationException("The streaming candidate returned a null IAsyncEnumerator.");
                }

                callerToken.ThrowIfCancellationRequested();

                bool hasFirst;
                try
                {
                    hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);

                    bool shouldRetrySameCandidate = await HandleCandidateFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        candidateIndex,
                        attempt,
                        eligible.Count,
                        callerToken).ConfigureAwait(false);

                    if (shouldRetrySameCandidate)
                    {
                        continue;
                    }

                    break;
                }

                if (callerToken.IsCancellationRequested)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    callerToken.ThrowIfCancellationRequested();
                }

                if (!hasFirst)
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        yield break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        bool shouldRetrySameCandidate = await HandleCandidateFailureBeforeFirstAsync(
                            ex,
                            candidate,
                            candidateIndex,
                            attempt,
                            eligible.Count,
                            callerToken).ConfigureAwait(false);

                        if (shouldRetrySameCandidate)
                        {
                            continue;
                        }

                        break;
                    }
                }

                ModelExecutionUpdate firstUpdate = enumerator.Current;
                if (firstUpdate is null)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    throw new InvalidOperationException("The streaming candidate yielded a null update.");
                }

                Exception? primaryFailure = null;
                try
                {
                    yield return firstUpdate;

                    while (true)
                    {
                        if (callerToken.IsCancellationRequested)
                        {
                            try
                            {
                                callerToken.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException ex)
                            {
                                primaryFailure = ex;
                                throw;
                            }
                        }

                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            primaryFailure = ex;
                            throw;
                        }

                        if (callerToken.IsCancellationRequested)
                        {
                            try
                            {
                                callerToken.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException ex)
                            {
                                primaryFailure = ex;
                                throw;
                            }
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        ModelExecutionUpdate update = enumerator.Current;
                        if (update is null)
                        {
                            primaryFailure = new InvalidOperationException("The streaming candidate yielded a null update.");
                            throw primaryFailure;
                        }

                        yield return update;
                    }
                }
                finally
                {
                    if (primaryFailure is not null)
                    {
                        try
                        {
                            await enumerator.DisposeAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Suppress secondary disposal exception to preserve primary failure
                        }
                    }
                    else
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                }

                yield break;
            }
        }
    }

    private async IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionCoreAsync(
        SessionState state_,
        ModelExecutionRequest request_,
        CancellationToken methodToken_,
        [EnumeratorCancellation] CancellationToken enumeratorToken_ = default)
    {
        using CancellationTokenSource? linkedCts =
            (methodToken_.CanBeCanceled && enumeratorToken_.CanBeCanceled && methodToken_ != enumeratorToken_)
                ? CancellationTokenSource.CreateLinkedTokenSource(methodToken_, enumeratorToken_)
                : null;

        CancellationToken callerToken = linkedCts is not null
            ? linkedCts.Token
            : (methodToken_.CanBeCanceled ? methodToken_ : enumeratorToken_);

        callerToken.ThrowIfCancellationRequested();

        if (!state_.TryAcquireTurn())
        {
            throw new InvalidOperationException("A turn is already in progress for this session.");
        }

        try
        {
            if (state_.IsEnded)
            {
                throw new InvalidOperationException("The session has ended.");
            }

            await EnsureBoundAsync(state_, callerToken).ConfigureAwait(false);

            ModelRouteCandidate candidate = state_.BoundCandidate!;
            IResumableModelSessionRuntime candidateRuntime = state_.BoundRuntime!;
            AgentSessionReference underlyingSession = state_.UnderlyingSession!;

            for (int attempt = 1; attempt <= this._resiliencePolicy.MaxAttemptsPerCandidate; attempt++)
            {
                callerToken.ThrowIfCancellationRequested();

                IAsyncEnumerable<ModelExecutionUpdate> stream;
                try
                {
                    stream = candidateRuntime.ExecuteStreamingInSessionAsync(underlyingSession, request_, callerToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await HandleSessionFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        attempt,
                        callerToken).ConfigureAwait(false);

                    continue;
                }

                if (stream is null)
                {
                    throw new InvalidOperationException("The streaming candidate returned a null IAsyncEnumerable.");
                }

                IAsyncEnumerator<ModelExecutionUpdate> enumerator;
                try
                {
                    enumerator = stream.GetAsyncEnumerator(callerToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await HandleSessionFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        attempt,
                        callerToken).ConfigureAwait(false);

                    continue;
                }

                if (enumerator is null)
                {
                    throw new InvalidOperationException("The streaming candidate returned a null IAsyncEnumerator.");
                }

                callerToken.ThrowIfCancellationRequested();

                bool hasFirst;
                try
                {
                    hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);

                    await HandleSessionFailureBeforeFirstAsync(
                        ex,
                        candidate,
                        attempt,
                        callerToken).ConfigureAwait(false);

                    continue;
                }

                if (callerToken.IsCancellationRequested)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    callerToken.ThrowIfCancellationRequested();
                }

                if (!hasFirst)
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        yield break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await HandleSessionFailureBeforeFirstAsync(
                            ex,
                            candidate,
                            attempt,
                            callerToken).ConfigureAwait(false);

                        continue;
                    }
                }

                ModelExecutionUpdate firstUpdate = enumerator.Current;
                if (firstUpdate is null)
                {
                    await DisposeSafelyAsync(enumerator).ConfigureAwait(false);
                    throw new InvalidOperationException("The streaming candidate yielded a null update.");
                }

                Exception? primaryFailure = null;
                try
                {
                    yield return firstUpdate;

                    while (true)
                    {
                        if (callerToken.IsCancellationRequested)
                        {
                            try
                            {
                                callerToken.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException ex)
                            {
                                primaryFailure = ex;
                                throw;
                            }
                        }

                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            primaryFailure = ex;
                            throw;
                        }

                        if (callerToken.IsCancellationRequested)
                        {
                            try
                            {
                                callerToken.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException ex)
                            {
                                primaryFailure = ex;
                                throw;
                            }
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        ModelExecutionUpdate update = enumerator.Current;
                        if (update is null)
                        {
                            primaryFailure = new InvalidOperationException("The streaming candidate yielded a null update.");
                            throw primaryFailure;
                        }

                        yield return update;
                    }
                }
                finally
                {
                    if (primaryFailure is not null)
                    {
                        try
                        {
                            await enumerator.DisposeAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Suppress secondary disposal exception to preserve primary failure
                        }
                    }
                    else
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                }

                yield break;
            }
        }
        finally
        {
            state_.ReleaseTurn();
        }
    }

    private async Task<bool> HandleCandidateFailureBeforeFirstAsync(
        Exception exception_,
        ModelRouteCandidate candidate_,
        int candidateIndex_,
        int attempt_,
        int totalCandidates_,
        CancellationToken cancellationToken_)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        bool isRetryable = this._failureClassifier.IsRetryable(candidate_, exception_);
        if (!isRetryable)
        {
            ExceptionDispatchInfo.Capture(exception_).Throw();
        }

        if (attempt_ < this._resiliencePolicy.MaxAttemptsPerCandidate)
        {
            if (this._resiliencePolicy.RetryBackoff > TimeSpan.Zero)
            {
                await Task.Delay(
                    this._resiliencePolicy.RetryBackoff,
                    this._timeProvider,
                    cancellationToken_).ConfigureAwait(false);
            }

            return true;
        }

        if (candidateIndex_ == totalCandidates_ - 1)
        {
            ExceptionDispatchInfo.Capture(exception_).Throw();
        }

        return false;
    }

    private async Task HandleSessionFailureBeforeFirstAsync(
        Exception exception_,
        ModelRouteCandidate candidate_,
        int attempt_,
        CancellationToken cancellationToken_)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        bool isRetryable = this._failureClassifier.IsRetryable(candidate_, exception_);
        if (!isRetryable)
        {
            ExceptionDispatchInfo.Capture(exception_).Throw();
        }

        if (attempt_ < this._resiliencePolicy.MaxAttemptsPerCandidate)
        {
            if (this._resiliencePolicy.RetryBackoff > TimeSpan.Zero)
            {
                await Task.Delay(
                    this._resiliencePolicy.RetryBackoff,
                    this._timeProvider,
                    cancellationToken_).ConfigureAwait(false);
            }

            return;
        }

        ExceptionDispatchInfo.Capture(exception_).Throw();
    }

    private static async ValueTask DisposeSafelyAsync(IAsyncDisposable? disposable_)
    {
        if (disposable_ is null)
        {
            return;
        }

        try
        {
            await disposable_.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress secondary disposal error to preserve primary failure
        }
    }

    private async Task EnsureBoundAsync(
        SessionState state_,
        CancellationToken cancellationToken_)
    {
        if (state_.IsBound)
        {
            return;
        }

        if (state_.IsEnded)
        {
            throw new InvalidOperationException("The session has ended.");
        }

        IReadOnlyList<ModelRouteCandidate> route =
            await AcquireRouteAsync(cancellationToken_).ConfigureAwait(false);

        ModelRouteCandidate? selectedCandidate = null;
        IResumableModelSessionRuntime? selectedRuntime = null;

        for (int i = 0; i < route.Count; i++)
        {
            if (route[i].Registration.Runtime is IResumableModelSessionRuntime resumableRuntime)
            {
                selectedCandidate = route[i];
                selectedRuntime = resumableRuntime;
                break;
            }
        }

        if (selectedCandidate is null || selectedRuntime is null)
        {
            throw new InvalidOperationException("No candidate in the route supports resumable sessions.");
        }

        AgentSessionReference underlyingSession;
        try
        {
            underlyingSession = selectedRuntime.CreateSession();
        }
        catch (Exception)
        {
            throw;
        }

        if (underlyingSession is null)
        {
            throw new InvalidOperationException("The candidate runtime returned a null session reference.");
        }

        state_.Bind(selectedCandidate, selectedRuntime, underlyingSession);
    }

    private sealed class SessionState
    {
        private readonly ConcurrentDictionary<string, SessionState> _ownerSessions;
        private readonly SemaphoreSlim _turnGate = new(1, 1);
        private bool _isEnded;
        private ModelRouteCandidate? _boundCandidate;
        private IResumableModelSessionRuntime? _boundRuntime;
        private AgentSessionReference? _underlyingSession;

        public AgentSessionReference SessionReference
        {
            get;
        }

        public bool IsEnded
        {
            get
            {
                return Volatile.Read(ref this._isEnded);
            }
        }

        public bool IsBound
        {
            get
            {
                return this._boundRuntime is not null;
            }
        }

        public ModelRouteCandidate? BoundCandidate
        {
            get
            {
                return this._boundCandidate;
            }
        }

        public IResumableModelSessionRuntime? BoundRuntime
        {
            get
            {
                return this._boundRuntime;
            }
        }

        public AgentSessionReference? UnderlyingSession
        {
            get
            {
                return this._underlyingSession;
            }
        }

        public SessionState(
            AgentSessionReference sessionReference_,
            ConcurrentDictionary<string, SessionState> ownerSessions_)
        {
            this.SessionReference =
                sessionReference_;
            this._ownerSessions =
                ownerSessions_;
        }

        public bool TryAcquireTurn()
        {
            if (this.IsEnded)
            {
                return false;
            }

            return this._turnGate.Wait(0);
        }

        public void ReleaseTurn()
        {
            this._turnGate.Release();
        }

        public void Bind(
            ModelRouteCandidate candidate_,
            IResumableModelSessionRuntime runtime_,
            AgentSessionReference underlyingSession_)
        {
            this._boundCandidate =
                candidate_;
            this._boundRuntime =
                runtime_;
            this._underlyingSession =
                underlyingSession_;
        }

        public async Task<bool> EndAsync(
            CancellationToken cancellationToken_)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            await this._turnGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
            try
            {
                cancellationToken_.ThrowIfCancellationRequested();

                if (this._isEnded)
                {
                    return false;
                }

                if (!this.IsBound)
                {
                    this._isEnded = true;
                    this._ownerSessions.TryRemove(this.SessionReference.Value, out _);
                    return true;
                }

                bool result = await this._boundRuntime!.EndSessionAsync(
                    this._underlyingSession!,
                    cancellationToken_).ConfigureAwait(false);

                this._isEnded = true;
                this._ownerSessions.TryRemove(this.SessionReference.Value, out _);
                return result;
            }
            finally
            {
                this._turnGate.Release();
            }
        }
    }
}
