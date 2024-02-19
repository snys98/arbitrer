using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arbitrer
{
  /// <summary>
  /// Represents an arbitrator that handles message routing and dispatching.
  /// </summary>
  public class Arbitrer : IArbitrer
  {
    private readonly ArbitrerOptions _options;
    private readonly IExternalMessageDispatcher _messageDispatcher;
    private readonly ILogger<Arbitrer> _logger;

    public Arbitrer(IOptions<ArbitrerOptions> options, IExternalMessageDispatcher messageDispatcher, ILogger<Arbitrer> logger)
    {
      this._options = options.Value;
      this._messageDispatcher = messageDispatcher;
      this._logger = logger;
    }

    /// <summary>
    /// Checks if the specified type has a local handler registered.
    /// </summary>
    /// <typeparam name="T">The type of the request.</typeparam>
    /// <returns>
    /// <see langword="true"/> if the specified type has a local handler registered; otherwise, <see langword="false"/>.
    /// </returns>
    public bool HasLocalHandler<T>() where T : IBaseRequest => this.HasLocalHandler(typeof(T));

    /// <summary>
    /// Checks if the given type has a local handler registered.
    /// </summary>
    /// <param name="t">The type to check.</param>
    /// <returns>True if the type has a local handler registered, False otherwise.</returns>
    public bool HasLocalHandler(Type t) => this._options.LocalRequests.Any(i => i == t);

    /// <summary>
    /// Determines if the specified type has a remote handler.
    /// </summary>
    /// <typeparam name="T">The type of the request.</typeparam>
    /// <returns>
    /// <c>true</c> if the specified type has a remote handler; otherwise, <c>false</c>.
    /// </returns>
    public bool HasRemoteHandler<T>() where T : IBaseRequest => this.HasRemoteHandler(typeof(T));

    /// <summary>
    /// Checks if the given type has a remote handler.
    /// </summary>
    /// <param name="t">The type to check.</param>
    /// <returns>True if the type has a remote handler; otherwise, false.</returns>
    public bool HasRemoteHandler(Type t) => this._options.RemoteRequests.Any(i => i == t);


    /// <summary>
    /// Retrieves the location of a handler specified by the type parameter.
    /// </summary>
    /// <typeparam name="T">The type of the handler.</typeparam>
    /// <returns>The location of the handler.</returns>
    public HandlerLocation GetLocation<T>() => this.GetLocation(typeof(T));

    /// <summary>
    /// Gets the location of the handler based on the specified behavior and type.
    /// </summary>
    /// <param name="t">The type to check for handler location.</param>
    /// <returns>The location of the handler (<see cref="HandlerLocation"/>).</returns>
    public HandlerLocation GetLocation(Type t)
    {
        if (t.IsAssignableTo(typeof(IRequest)))
        {
            return HandlerLocation.Local;
        }
        if (t.IsAssignableTo(typeof(INotification)))
        {
            return HandlerLocation.Remote;
        }
        return HandlerLocation.NotFound;
    }

    /// <summary>
    /// Invokes a remote handler to process a request and returns the response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <param name="request">The request object.</param>
    /// <returns>The response object.</returns>
    public async Task<TResponse> InvokeRemoteHandler<TRequest, TResponse>(TRequest request)
    {
      _logger.LogDebug($"Invoking remote handler for: {typeof(TRequest).TypeQueueName(_options)}");
      var result = await _messageDispatcher.Dispatch<TRequest, TResponse>(request);
      _logger.LogDebug($"Remote request for {typeof(TRequest).TypeQueueName(_options)} completed!");

      if (result.Status == Messages.StatusEnum.Exception)
      {
        throw result.Exception ?? new Exception("Error executing remote command") ;
      }

      return result.Content;
    }

    /// <summary>
    /// Sends a remote notification.
    /// </summary>
    /// <typeparam name="TRequest">The type of the notification.</typeparam>
    /// <param name="request">The notification request to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SendRemoteNotification<TRequest>(TRequest request) where TRequest : INotification
    {
      _logger.LogDebug($"Invoking remote handler for: {typeof(TRequest).TypeQueueName(_options)}");
      await _messageDispatcher.Notify(request);
      _logger.LogDebug($"Remote request for {typeof(TRequest).TypeQueueName(_options)} completed!");
    }

    /// <summary>
    /// Retrieves the local request types.
    /// </summary>
    /// <returns>
    /// An IEnumerable of Type containing the local request types.
    /// </returns>
    public IEnumerable<Type> GetLocalRequestsTypes() => _options.LocalRequests;

    /// <summary>
    /// Retrieves the collection of remote request types.
    /// </summary>
    /// <returns>
    /// Returns an enumerable collection of <see cref="Type"/> objects representing remote request types.
    /// </returns>
    public IEnumerable<Type> GetRemoteRequestsTypes() => _options.RemoteRequests;
  }

  public enum HandlerLocation
  {
    NotFound,
    Local,
    Remote,
  }
}