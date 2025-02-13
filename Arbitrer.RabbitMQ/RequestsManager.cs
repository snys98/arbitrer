using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Arbitrer.RabbitMQ
{
  /// <summary>
  /// The RequestsManager class is responsible for managing requests and notifications in a distributed system. It implements the IHostedService interface.
  /// </summary>
  public class RequestsManager : IHostedService
  {
    private ArbitrerOptions _arbitrerOptions;

    /// <summary>
    /// Represents the logger for the RequestsManager class.
    /// </summary>
    /// <typeparam name="RequestsManager">The type of the class using the logger.</typeparam>
    private readonly ILogger<RequestsManager> _logger;

    /// <summary>
    /// The private readonly field that holds an instance of the IArbitrer interface.
    /// </summary>
    private readonly IArbitrer _arbitrer;

    /// <summary>
    /// Represents a service provider.
    /// </summary>
    private readonly IServiceProvider _provider;

    /// <summary>
    /// Represents a private instance of an IConnection object.
    /// </summary>
    private IConnection _connection = null;

    /// <summary>
    /// Represents the channel used for communication.
    /// </summary>
    private IModel _channel = null;

    /// <summary>
    /// A HashSet used for deduplication cache.
    /// </summary>
    private readonly HashSet<string> _deduplicationcache = new HashSet<string>();

    /// <summary>
    /// Represents a SHA256 hash algorithm instance used for hashing data.
    /// </summary>
    private readonly SHA256 _hasher = SHA256.Create();

    /// <summary>
    /// Represents the options for the message dispatcher.
    /// </summary>
    private readonly MessageDispatcherOptions _options;

    /// <summary>
    /// Constructs a new instance of the RequestsManager class.
    /// </summary>
    /// <param name="options">The options for the message dispatcher.</param>
    /// <param name="logger">The logger to be used for logging.</param>
    /// <param name="arbitrer">The object responsible for coordinating requests.</param>
    /// <param name="provider">The service provider for resolving dependencies.</param>
    public RequestsManager(IOptions<MessageDispatcherOptions> options, ILogger<RequestsManager> logger, IArbitrer arbitrer, IServiceProvider provider,
      IOptions<ArbitrerOptions> arbitrerOptions)
    {
      this._arbitrerOptions = arbitrerOptions.Value;
      this._options = options.Value;
      this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
      this._arbitrer = arbitrer;
      this._provider = provider;
    }

    /// <summary>
    /// Starts the asynchronous process of connecting to RabbitMQ and consuming messages from queues.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
      if (_connection == null)
      {
        _logger.LogInformation($"ARBITRER: Creating RabbitMQ Conection to '{_options.HostName}'...");
        var factory = new ConnectionFactory
        {
          HostName = _options.HostName,
          UserName = _options.UserName,
          Password = _options.Password,
          VirtualHost = _options.VirtualHost,
          Port = _options.Port,
          DispatchConsumersAsync = true,
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(Constants.ArbitrerExchangeName, ExchangeType.Topic);

        _logger.LogInformation("ARBITRER: ready !");
      }


      foreach (var t in _arbitrer.GetLocalRequestsTypes())
      {
        if (t is null) continue;
        var isNotification = typeof(INotification).IsAssignableFrom(t);
        var queueName = $"{t.TypeQueueName(_arbitrerOptions)}${(isNotification ? Guid.NewGuid().ToString() : "")}";

        _channel.QueueDeclare(queue: queueName, durable: _options.Durable, exclusive: isNotification, autoDelete: _options.AutoDelete, arguments: null);
        _channel.QueueBind(queueName, Constants.ArbitrerExchangeName, t.TypeQueueName(_arbitrerOptions));


        var consumer = new AsyncEventingBasicConsumer(_channel);

        var consumerMethod = typeof(RequestsManager)
          .GetMethod(isNotification ? "ConsumeChannelNotification" : "ConsumeChannelMessage", BindingFlags.Instance | BindingFlags.NonPublic)?
          .MakeGenericMethod(t);

        consumer.Received += async (s, ea) =>
        {
          try
          {
            if (consumerMethod != null)
              await (Task)consumerMethod.Invoke(this, new object[] { s, ea });
          }
          catch (Exception e)
          {
            _logger.LogError(e, e.Message);
            throw;
          }
        };
        _channel.BasicConsume(queue: queueName, autoAck: isNotification, consumer: consumer);
      }

      _channel.BasicQos(0, 1, false);
      return Task.CompletedTask;
    }

    /// <summary>
    /// ConsumeChannelNotification is a private asynchronous method that handles the consumption of channel notifications. </summary>
    /// <typeparam name="T">The type of messages to be consumed</typeparam> <param name="sender">The object that triggered the event</param> <param name="ea">The event arguments containing the consumed message</param>
    /// <returns>A Task representing the asynchronous operation</returns>
    /// /
    private async Task ConsumeChannelNotification<T>(object sender, BasicDeliverEventArgs ea)
    {
      var mediator = _provider.CreateScope().ServiceProvider.GetRequiredService<IMediator>();
      var arbitrer = mediator as ArbitredMediatr;
      try
      {
        var msg = ea.Body.ToArray();

        if (_options.DeDuplicationEnabled)
        {
          var hash = msg.GetHash(_hasher);
          lock (_deduplicationcache)
            if (_deduplicationcache.Contains(hash))
            {
              _logger.LogDebug($"duplicated message received : {ea.Exchange}/{ea.RoutingKey}");
              return;
            }

          lock (_deduplicationcache)
            _deduplicationcache.Add(hash);

          // Do not await this task
#pragma warning disable CS4014
          Task.Run(async () =>
          {
            await Task.Delay(_options.DeDuplicationTTL);
            lock (_deduplicationcache)
              _deduplicationcache.Remove(hash);
          });
#pragma warning restore CS4014
        }

        _logger.LogDebug("Elaborating notification : {0}", Encoding.UTF8.GetString(msg));
        var message = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(msg), _options.SerializerSettings);

        var replyProps = _channel.CreateBasicProperties();
        replyProps.CorrelationId = ea.BasicProperties.CorrelationId;

        arbitrer?.StopPropagating();
        await mediator.Publish(message);

      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"Error executing message of type {typeof(T)} from external service");
      }
      finally
      {
        arbitrer?.ResetPropagating();
      }
    }

    /// <summary>
    /// Consumes a message from a channel and processes it asynchronously.
    /// </summary>
    /// <typeparam name="T">The type of the message being consumed.</typeparam>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="ea">An object that contains the event data.</param>
    /// <returns>A task representing the asynchronous processing of the message.</returns>
    /// <remarks>
    /// This method deserializes the message using the specified <c>DeserializerSettings</c>,
    /// sends it to the mediator for processing, and publishes a response message to the
    /// specified reply-to queue. If an exception occurs during processing, an error response
    /// message will be published.
    /// </remarks>
    private async Task ConsumeChannelMessage<T>(object sender, BasicDeliverEventArgs ea)
    {
      string responseMsg = null;
      var replyProps = _channel.CreateBasicProperties();
      try
      {
        replyProps.CorrelationId = ea.BasicProperties.CorrelationId;

        var msg = ea.Body.ToArray();
        _logger.LogDebug("Elaborating message : {0}", Encoding.UTF8.GetString(msg));
        var message = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(msg), _options.SerializerSettings);


        var mediator = _provider.CreateScope().ServiceProvider.GetRequiredService<IMediator>();
        var response = await mediator.Send(message);
        responseMsg = JsonSerializer.Serialize(new Messages.ResponseMessage { Content = response, Status = Messages.StatusEnum.Ok },
          _options.SerializerSettings);
        _logger.LogDebug("Elaborating sending response : {0}", responseMsg);
      }
      catch (Exception ex)
      {
        responseMsg = JsonSerializer.Serialize(new Messages.ResponseMessage { Exception = ex, Status = Messages.StatusEnum.Exception },
          _options.SerializerSettings);
        _logger.LogError(ex, $"Error executing message of type {typeof(T)} from external service");
      }
      finally
      {
        _channel.BasicPublish(exchange: "", routingKey: ea.BasicProperties.ReplyTo, basicProperties: replyProps,
          body: Encoding.UTF8.GetBytes(responseMsg ?? ""));
        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
      }
    }

    /// <summary>
    /// Stops the asynchronous operation and closes the channel and connection.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
      try
      {
        _channel?.Close();
      }
      catch
      {
      }

      try
      {
        _connection?.Close();
      }
      catch
      {
      }

      return Task.CompletedTask;
    }
  }
}