using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arbitrer
{
  public static class ArbitrerExtensions
  {
    public static IServiceCollection AddArbitrer(this IServiceCollection services, Action<ArbitrerOptions> configure = null)
    {
      if (configure != null)
        services.Configure<ArbitrerOptions>(configure);
      services.AddScoped(typeof(IPipelineBehavior<,>), typeof(Pipelines.ArbitrerPipeline<,>));
      services.AddSingleton<IArbitrer, Arbitrer>();
      return services;
    }


    public static ArbitrerOptions SetAsLocalRequest<T>(this ArbitrerOptions options) where T : IBaseRequest
    {
      options.LocalRequests.Add(typeof(T));
      return options;
    }

    public static ArbitrerOptions ListenForNotification<T>(this ArbitrerOptions options) where T : INotification
    {
      options.LocalRequests.Add(typeof(T));
      return options;
    }

    public static ArbitrerOptions SetAsRemoteRequest<T>(this ArbitrerOptions options) where T : IBaseRequest
    {
      options.RemoteRequests.Add(typeof(T));
      return options;
    }

    public static ArbitrerOptions PropagateNotification<T>(this ArbitrerOptions options) where T : INotification
    {
      options.RemoteRequests.Add(typeof(T));
      return options;
    }

    public static ArbitrerOptions SetAsLocalRequests(this ArbitrerOptions options, Func<IEnumerable<Assembly>> assemblySelect)
    {
      var types = (from a in assemblySelect()
        from t in a.GetTypes()
        where typeof(IBaseRequest).IsAssignableFrom(t) || typeof(INotification).IsAssignableFrom(t)
        select t).AsEnumerable();
      foreach (var t in types)
        options.LocalRequests.Add(t);
      return options;
    }

    public static ArbitrerOptions SetAsLocalRequests(this ArbitrerOptions options, Func<IEnumerable<Type>> typesSelect)
    {
      foreach (var t in typesSelect())
        options.LocalRequests.Add(t);
      return options;
    }

    public static ArbitrerOptions SetAsRemoteRequests(this ArbitrerOptions options, Func<IEnumerable<Assembly>> assemblySelect)
    {
      var types = (from a in assemblySelect()
        from t in a.GetTypes()
        where typeof(IBaseRequest).IsAssignableFrom(t) || typeof(INotification).IsAssignableFrom(t)
        select t).AsEnumerable();
      foreach (var t in types)
        options.RemoteRequests.Add(t);
      return options;
    }

    public static ArbitrerOptions SetAsRemoteRequests(this ArbitrerOptions options, Func<IEnumerable<Type>> typesSelect)
    {
      foreach (var t in typesSelect())
        options.RemoteRequests.Add(t);
      return options;
    }
  }
}