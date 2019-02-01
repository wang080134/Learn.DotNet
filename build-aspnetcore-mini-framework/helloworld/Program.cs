﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Text;

namespace helloworld
{
    class Program
    {
        static void Main()
        {
            new WebHostBuilder()
                .UseHttpListener()
                .Configure(app => app.Use(HelloMiddleware).Use(WorldMiddleware))
                .Build()
                .Run();
        }

        public static RequestDelegate HelloMiddleware(RequestDelegate next)
        {
            return context => {
                context.Response.Write("Hello ");
                return next(context);
            };
        }

        public static RequestDelegate WorldMiddleware(RequestDelegate next)
        {
            return context => {
                context.Response.Write("World!");
                return Task.CompletedTask; 
            };
        }
    }

    public delegate Task RequestDelegate(HttpContext context);

    public interface  IApplicationBuilder
    {
        IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware);
        RequestDelegate Build();
    }

    public class ApplicationBuilder : IApplicationBuilder
    {
        private readonly List<Func<RequestDelegate, RequestDelegate>> _middlewares = new List<Func<RequestDelegate, RequestDelegate>>();
        /* 
        public RequestDelegate Build()
        {
            _middlewares.Reverse();
            return httpContext => {
                RequestDelegate next = _ => { 
                    _.Response.StatusCode = 404; 
                    return Task.CompletedTask; 
                };
                foreach (var middleware in _middlewares)
                {
                    next = middleware(next);
                }
                return next(httpContext);
            }; 
        }*/
        public RequestDelegate Build()
        {
            _middlewares.Reverse();
            RequestDelegate next = _ => { 
                _.Response.StatusCode = 404; 
                return Task.CompletedTask; 
            };
            foreach (var middleware in _middlewares)
            {
                next = middleware(next);
            }
            return next;
        }

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            _middlewares.Add(middleware);
            return this;
        }
    }

    public interface IServer
    { 
        Task Run(RequestDelegate handler);
    }

    public interface IFeatureCollection : IDictionary<Type, object> { }
    public class FeatureCollection : Dictionary<Type, object>, IFeatureCollection { }   
    public static partial class Extensions
    {
        public static T Get<T>(this IFeatureCollection features) 
        { 
            return features.TryGetValue(typeof(T), out var value) ? (T)value : default(T);
        }
        public static IFeatureCollection Set<T>(this IFeatureCollection features, T feature)
        { 
            features[typeof(T)] = feature;
            return features;
        }
    }

    public interface IHttpRequestFeature
    {
        Uri Url { get; }
        NameValueCollection Headers { get; }
        Stream Body { get; }
    }
    public interface IHttpResponseFeature
    {
        int StatusCode { get; set; }
        NameValueCollection Headers { get; }
        Stream Body { get; }
    }

    public class HttpRequest
    {
        private readonly IHttpRequestFeature _feature;    
        
        public  Uri Url => _feature.Url;
        public  NameValueCollection Headers => _feature.Headers;
        public  Stream Body => _feature.Body;

        public HttpRequest(IFeatureCollection features) => _feature = features.Get<IHttpRequestFeature>();
    }

    public class HttpResponse
    {
        private readonly IHttpResponseFeature _feature;

        public  NameValueCollection Headers => _feature.Headers;
        public  Stream Body => _feature.Body;
        public int StatusCode { get => _feature.StatusCode; set => _feature.StatusCode = value; }

        public HttpResponse(IFeatureCollection features) => _feature = features.Get<IHttpResponseFeature>();
    }

    public class HttpContext
    {
        public  HttpRequest Request { get; }
        public  HttpResponse Response { get; }
        public HttpContext(IFeatureCollection features)
        {
            Request = new HttpRequest(features);
            Response = new HttpResponse(features);
        }
    }

    public class HttpListenerFeature : IHttpRequestFeature, IHttpResponseFeature
    {
        private readonly HttpListenerContext _context;
        public HttpListenerFeature(HttpListenerContext context) => _context = context;

        Uri IHttpRequestFeature.Url => _context.Request.Url;
        NameValueCollection IHttpRequestFeature.Headers => _context.Request.Headers;
        NameValueCollection IHttpResponseFeature.Headers => _context.Response.Headers;
        Stream IHttpRequestFeature.Body => _context.Request.InputStream;
        Stream IHttpResponseFeature.Body => _context.Response.OutputStream;
        int IHttpResponseFeature.StatusCode { get => _context.Response.StatusCode; set => _context.Response.StatusCode = value; }
    }

    public class HttpListenerServer : IServer
    {
        private readonly HttpListener _httpListener;
        private readonly string[] _urls;

        public HttpListenerServer(params string[] urls)
        {
            _httpListener = new HttpListener();
            _urls = urls.Any() ? urls: new string[] { "http://localhost:5000/" };
        }

        public Task Run(RequestDelegate handler)
        {
            Array.ForEach(_urls, url => _httpListener.Prefixes.Add(url));
            _httpListener.Start();
            while (true)
            {
                var listenerContext = _httpListener.GetContext(); 
                var feature = new HttpListenerFeature(listenerContext);
                var features = new FeatureCollection()
                    .Set<IHttpRequestFeature>(feature)
                    .Set<IHttpResponseFeature>(feature);
                var httpContext = new HttpContext(features);
                handler(httpContext);
                listenerContext.Response.Close();
            }
        }
    }

    public interface IWebHost
    {
        Task Run();
    }

    public class WebHost : IWebHost
    {
        private readonly IServer _server;
        private readonly RequestDelegate _handler; 
        public WebHost(IServer server, RequestDelegate handler)
        {
            _server = server;
            _handler = handler;
        } 
        public Task Run()
        {
            return _server.Run(_handler);
        }
    }

    public interface IWebHostBuilder
    {
        IWebHostBuilder UseServer(IServer server);
        IWebHostBuilder Configure(Action<IApplicationBuilder> configure);
        IWebHost Build();
    }

    public class WebHostBuilder : IWebHostBuilder
    {
        private IServer _server;
        private readonly List<Action<IApplicationBuilder>> _configures = new List<Action<IApplicationBuilder>>();   

        public IWebHostBuilder Configure(Action<IApplicationBuilder> configure)
        {
            _configures.Add(configure);
            return this;
        }
        public IWebHostBuilder UseServer(IServer server)
        {
            _server = server;
            return this;
        }   

        public IWebHost Build()
        {
            var builder = new ApplicationBuilder();
            foreach (var configure in _configures)
            {
                configure(builder);
            }
            return new WebHost(_server, builder.Build());
        }
    }

    public static partial class Extensions
    {
        public static IWebHostBuilder UseHttpListener(this IWebHostBuilder builder, params string[] urls)
        {
            return builder.UseServer(new HttpListenerServer(urls));
        }
        public static Task Write(this HttpResponse response, string contents)
        {
            var buffer = Encoding.UTF8.GetBytes(contents);
            return response.Body.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}
