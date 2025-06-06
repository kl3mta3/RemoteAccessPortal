﻿using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteAccessPortal.Database;
using RemoteAccessPortal.Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace RemoteAccessPortal.Dashboard
{
    public class WebAppDashboard
    {

        internal static string WebAppIP { get; set; }
        internal static int WebAppPort { get; set; }
        internal static int ClientPort { get; set; }
        internal static string AdminUserHash { get; set; }



        public static WebApplication CreateWebApp(string[] args, int port)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Error);

            // CORS Policy
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            builder.Services.AddRazorPages(options =>
            {
                options.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());
            });

            builder.Services.AddAuthorization();
            builder.Services.AddScoped<DatabaseManager>();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Parse(WebAppIP), port); // HTTP
                //options.Listen(IPAddress.Parse(WebAppIP), port + 1, listen =>
                //{
                //    listen.UseHttps();
                //});
            });

            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(15);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            var app = builder.Build();

            app.UseCors();



            app.Use(async (context, next) =>
            {
                var remoteIp = context.Connection.RemoteIpAddress?.ToString();

                if (string.IsNullOrWhiteSpace(remoteIp) ||
                    (!remoteIp.StartsWith("10.") &&
                     !remoteIp.StartsWith("192.168.") &&
                     !remoteIp.StartsWith("127.")))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Forbidden");
                    return;
                }

                await next();
            });


            if (port == ClientPort)
            {
                app.MapGet("/", () => Results.Ok("The Portal is Running"));


                app.MapPost("/update", async (HttpContext context) =>
                {



                    if (!context.Request.Headers.TryGetValue("X-API-Token", out var token) || !await DatabaseManager.UserApiKeyExists(token))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden");
                        return Results.Json(null);
                    }

                    var cancellationToken = context.RequestAborted;
                    cancellationToken.Register(() =>
                    {
                        throw new OperationCanceledException("Client disconnected mid-response!");
                    });





                    var clients = DatabaseManager.Clients; ;

                    return Results.Json(clients);
                });



                app.MapPost("/logon", async (HttpContext context, NewLogonRequest payload) =>
                {
                

                    string userHash = payload.HashedUsername;
                    string password = payload.HashedPassword;
                    if ( string.IsNullOrWhiteSpace(password))
                    {

                           context.Response.StatusCode = 403;
                           await context.Response.WriteAsync("Forbidden: Password Missing.");
                           return Results.Json(null); ;
                    }

                    User user = await DatabaseManager.GetUserByUserHash(userHash);

                    if (user == null)
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: User not found");
                        return Results.Json(null);
                    }

                    if (string.IsNullOrWhiteSpace(userHash))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: Username Incorrect");
                        return Results.Json(null);
                    }

                    if (!Config.Config.VerifyPassword(password, user.PasswordHash))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: Password Incorrect");
                        return Results.Json(null);
                    }

                    string ApiKey = await DatabaseManager.GetUserApiKeyWithUserHash(userHash);

                    if (string.IsNullOrWhiteSpace(ApiKey))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: ApiKey Missing");
                        return Results.Json(null);
                    }


                    if (!context.Request.Headers.TryGetValue("X-API-Token", out var token) || token != ApiKey)
                    {
                        Console.WriteLine($"ApiKey: {token} does not match {ApiKey} for user {user.Username} with userHash {user.UserHash}");
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: ApiKey Incorrect");
                        return Results.Json(null);
                    }

                    var cancellationToken = context.RequestAborted;
                    cancellationToken.Register(() =>
                    {
                        throw new OperationCanceledException("Client disconnected mid-response!");
                    });

                    var clients = DatabaseManager.Clients;

                    return Results.Json(clients);
                });



                app.MapPost("/alert", async (HttpContext context, NewAlertRequest alert) =>
                {


                    if (!context.Request.Headers.TryGetValue("X-API-Token", out var token) || !await DatabaseManager.UserApiKeyExists(token))
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden");
                        return Results.Json(null);
                    }


                    var cancellationToken = context.RequestAborted;

                    cancellationToken.Register(() =>
                    {
                        throw new OperationCanceledException("Client disconnected mid-response!");
                    });

                    await DatabaseManager.InsertAlert(alert.ClientName, alert.AddedBy, alert.Message);

                    return Results.Ok(new { success = true });
                });


            }
            else if (port == WebAppPort)
            {
               

                app.MapRazorPages();


                app.UseStaticFiles();
                app.UseSession();
                app.UseRouting();
                app.UseAuthorization();
                app.MapRazorPages();
                //app.MapGet("/", context =>
                //{
                //    context.Response.Redirect("/Index");
                //    return Task.CompletedTask;
                //});


            }
            return app;
        }

        public static async Task<List<Client>> GetClientList()
        {
            List<Client> clients =  DatabaseManager.Clients;
            return clients;
        }

        public static async Task CreateAlert(NewAlertRequest alert)
        {
       
            await DatabaseManager.InsertAlert(alert.ClientName, alert.AddedBy, alert.ClientName);
        }

    }









}
