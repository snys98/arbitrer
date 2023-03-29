using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Arbitrer
{
  public class ArbitredMediatr : Mediator
  {
    private readonly IArbitrer arbitrer;
    private readonly ILogger<ArbitredMediatr> logger;
    private bool allowRemoteRequest = true;

    // public ArbitredMediatr(ServiceFactory serviceFactory, IArbitrer arbitrer, ILogger<ArbitredMediatr> logger) : base(serviceFactory)
    // {
    //   this.arbitrer = arbitrer;
    //   this.logger = logger;
    // }
    
    public ArbitredMediatr(IServiceProvider serviceProvider, IArbitrer arbitrer, ILogger<ArbitredMediatr> logger) : base(serviceProvider)
    {
      this.arbitrer = arbitrer;
      this.logger = logger;
    }

    public void StopPropagating()
    {
      allowRemoteRequest = false;
    }

    public void ResetPropagating()
    {
      allowRemoteRequest = true;
    }

    protected override async Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
      if (allowRemoteRequest && arbitrer.HasRemoteHandler(notification.GetType()))
      {
        logger.LogDebug("Propagating: {Json}", JsonConvert.SerializeObject(notification));
        await arbitrer.SendRemoteNotification(notification);
      }
      else
        await base.PublishCore(handlerExecutors, notification, cancellationToken);
    }

    // protected override async Task PublishCore(IEnumerable<Func<INotification, CancellationToken, Task>> allHandlers, INotification notification,
    //   CancellationToken cancellationToken)
    // {
    //   if (allowRemoteRequest && arbitrer.HasRemoteHandler(notification.GetType()))
    //   {
    //     logger.LogDebug("Propagating: {Json}", JsonConvert.SerializeObject(notification));
    //     await arbitrer.SendRemoteNotification(notification);
    //   }
    //   else
    //     await base.PublishCore(allHandlers, notification, cancellationToken);
    // }
  }
}