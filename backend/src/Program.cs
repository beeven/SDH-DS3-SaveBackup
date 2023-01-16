using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Extensions.Configuration;
using System.CommandLine;
using System.Text.Json;
using System.IO.Pipes;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;

public class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("OneDrive plugin for DS3SaveBackup");
        var configFileOption = new Option<string>(
            name: "--config",
            description: "The config file. Default to appsettings.json",
            getDefaultValue: () => "appsettings.json"
            );

        var zmqPortOption = new Option<int>(
            name: "--port",
            description: "The port zmq to connect.",
            getDefaultValue: () => 0
        );

        //configFileOption.SetDefaultValue("appsettings.json");
        rootCommand.AddGlobalOption(configFileOption);
        rootCommand.AddGlobalOption(zmqPortOption);
        // rootCommand.SetHandler((config)=>{

        // }, configFileOption);


        var uploadCommand = new Command("upload", "Upload file to cloud.");
        rootCommand.AddCommand(uploadCommand);

        var downloadCommand = new Command("download", "Download file from cloud.");
        rootCommand.AddCommand(downloadCommand);

        var loginCommand = new Command("login", "Login via device code flow.");
        rootCommand.AddCommand(loginCommand);

        var checkCommand = new Command("check", "Check whether user is logged in.");
        rootCommand.AddCommand(checkCommand);



        loginCommand.SetHandler(async (context) =>
        {
            var configFile = context.ParseResult.GetValueForOption(configFileOption);
            var config = ParseConfig(configFile);
            var port = context.ParseResult.GetValueForOption(zmqPortOption);

            var app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithRedirectUri("http://localhost")
            .Build();


            var storageProperties =
                new StorageCreationPropertiesBuilder("DS3SaveBackup_cache.json", config.WorkingDirectory)
                    .WithUnprotectedFile()
                    .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);


            var accounts = await app.GetAccountsAsync();
            var strAccounts = string.Join("\n\t", accounts.Select(x => x.Username));
            //Console.WriteLine($"Cached accounts:\n\t{strAccounts}");
            AuthenticationResult? result = null;
            string? ErrorMessage = null;

            NetMQ.Sockets.RequestSocket? sock = null;

            if (port > 0 && port < 65536)
            {
                sock = new NetMQ.Sockets.RequestSocket("tcp://127.0.0.1:" + port.ToString());
            }

            try
            {
                result = await app.AcquireTokenSilent(config.Scopes, accounts.FirstOrDefault()).ExecuteAsync();
                //Console.WriteLine(@$"Acquired token silently.");
                sock?.SendFrame(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<AuthResult>(new AuthResult
                {
                    Ok = true,
                    DisplayName = result.Account.Username,
                    Id = result.Account.HomeAccountId.ObjectId
                }));
            }
            catch (MsalUiRequiredException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine($"Interactive login required.");
                //System.Console.WriteLine($"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&scope=user.read");




                result = await app.AcquireTokenInteractive(config.Scopes)
                                    .WithSystemWebViewOptions(GetCustomHTML(sock))
                                    .ExecuteAsync();
            }
            catch (OperationCanceledException ex)
            {
                ErrorMessage = ex.Message;
                //Console.Error.WriteLine(ex.Message);
            }
            catch (MsalServiceException ex)
            {
                ErrorMessage = ex.Message;
                //Console.Error.WriteLine(ex.Message);
            }
            catch (MsalClientException ex)
            {
                ErrorMessage = ex.Message;
                //Console.Error.WriteLine(ex.Message);
            }

            if (result is null)
            {
                //Console.Error.WriteLine("Authentication failed.");
                var res = new AuthResult
                {
                    Ok = false,
                    Error = ErrorMessage ?? "Authentication failed."
                };
                System.Console.WriteLine(JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
                sock?.SendFrame(ErrorMessage ?? "Authentication failed.");
                context.ExitCode = 1;
            }
            else
            {

                System.Console.WriteLine(JsonSerializer.Serialize(new AuthResult
                {
                    Ok = true,
                    DisplayName = result.Account.Username,
                    Id = result.UniqueId
                }, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
            }

            sock?.SendFrame("done");

            if (sock is not null)
            {
                sock.Close();
                sock.Dispose();
            }

        });


        uploadCommand.SetHandler(async (context) =>
        {
            var configFile = context.ParseResult.GetValueForOption(configFileOption);
            var config = ParseConfig(configFile);
            var port = context.ParseResult.GetValueForOption(zmqPortOption);

            using var sock = new NetMQ.Sockets.PushSocket("tcp://127.0.0.1:" + port.ToString());

            var app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithRedirectUri("http://localhost")
            .Build();

            var storageProperties =
                new StorageCreationPropertiesBuilder("DS3SaveBackup_cache.json", config.WorkingDirectory)
                    .WithUnprotectedFile()
                    .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);

            var accounts = await app.GetAccountsAsync();
            var strAccounts = string.Join("\n\t", accounts.Select(x => x.Username));
            //Console.WriteLine($"Cached accounts:\n\t{strAccounts}");
            AuthenticationResult? result = null;

            try
            {
                result = await app.AcquireTokenSilent(config.Scopes, accounts.FirstOrDefault()).ExecuteAsync();
                //Console.WriteLine(@$"Acquired token silently.");
            }
            catch (MsalUiRequiredException ex)
            {
                sock.SendFrame(ex.Message);
                sock.SendFrame("done");
                Console.Error.WriteLine("Login Required: {0}", ex.Message);

                context.ExitCode = 1;

                return;
            }

            var graphClient = new GraphServiceClient(new DelegateAuthenticationProvider((requestMessage) =>
            {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", result?.AccessToken);
                return Task.FromResult(0);
            }));
            try
            {
                var msgToSend = new BlockingCollection<string>();

                var uploadTask = Task.Run(async () =>
                {
                    foreach (var filename in System.IO.Directory.GetFiles(config.LocalFolder))
                    {
                        var fn = System.IO.Path.GetFileName(filename);
                        System.Console.WriteLine($"Uploading {fn}");
                        sock.SendFrame($"Uploading {fn}");
                        var name = System.IO.Path.GetFileName(filename);
                        using var fs = System.IO.File.OpenRead(filename);

                        var uploadSession = await graphClient.Me.Drive.Root.ItemWithPath(config.CloudFolder + "\\" + name)
                            .CreateUploadSession(new DriveItemUploadableProperties
                            {
                                AdditionalData = new Dictionary<string, object>
                                {
                                    {"@microsoft.graph.conflictBehavior", "replace"}
                                }
                            }).Request().PostAsync();

                        var fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, fs);
                        var totalLength = fs.Length;

                        IProgress<long> progress = new Progress<long>(prog =>
                        {
                            System.Console.WriteLine($"Uploaded {prog} bytes of {totalLength} bytes");
                            msgToSend.Add($"Uploaded {prog} bytes of {totalLength} bytes");
                        });

                        await fileUploadTask.UploadAsync(progress);

                        System.Console.WriteLine($"Upload completed.");
                        sock.SendFrame("Upload completed.");
                    }
                }).ContinueWith(async (t) =>
                {
                    await t;
                    msgToSend.Add("done");
                    msgToSend.CompleteAdding();
                });

                var sendMsgTask = Task.Run(() =>
                {
                    while (!msgToSend.IsAddingCompleted)
                    {
                        if (msgToSend.TryTake(out string? msg, TimeSpan.FromSeconds(1)))
                        {
                            if (msg is not null)
                            {
                                sock.SendFrame(msg);
                            }
                        }
                    }
                });

                await Task.WhenAll(uploadTask, sendMsgTask);

                //var resp = await graphClient.Me.Drive.Root.ItemWithPath(config.CloudFolder + "\\" + name).

            }
            catch (ServiceException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.RawResponseBody);
                sock.SendFrame(ex.Message);
                sock.SendFrame(ex.RawResponseBody);
                context.ExitCode = 1;
            }
            sock.SendFrame("done");

        });



        downloadCommand.SetHandler(async (context) =>
        {
            var configFile = context.ParseResult.GetValueForOption(configFileOption);
            var config = ParseConfig(configFile);
            var port = context.ParseResult.GetValueForOption(zmqPortOption);

            PushSocket? sock = null;

            if (port > 0 && port < 65536)
            {
                sock = new PushSocket("tcp://localhost:" + port.ToString());
            }


            var app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithRedirectUri("http://localhost")
            .Build();

            var storageProperties =
                new StorageCreationPropertiesBuilder("DS3SaveBackup_cache.json", config.WorkingDirectory)
                    .WithUnprotectedFile()
                    .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);

            var accounts = await app.GetAccountsAsync();
            var strAccounts = string.Join("\n\t", accounts.Select(x => x.Username));
            //Console.WriteLine($"Cached accounts:\n\t{strAccounts}");
            AuthenticationResult? result = null;

            try
            {
                result = await app.AcquireTokenSilent(config.Scopes, accounts.FirstOrDefault()).ExecuteAsync();
                //Console.WriteLine(@$"Acquired token silently.");
            }
            catch (MsalUiRequiredException ex)
            {
                Console.Error.WriteLine("Login Required: {0}", ex.Message);
                context.ExitCode = 1;
                sock?.SendFrame("Login required.");
                sock?.SendFrame("done");
                return;
            }

            var graphClient = new GraphServiceClient(new DelegateAuthenticationProvider((requestMessage) =>
            {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", result?.AccessToken);
                return Task.FromResult(0);
            }));
            try
            {
                var items = await graphClient.Me.Drive.Root.ItemWithPath(config.CloudFolder).Children.Request().GetAsync();
                foreach (var item in items.Where(x => x.File != null))
                {
                    System.Console.Write($"Downloading {item.Name}...");
                    sock?.SendFrame($"Downloading {item.Name}...");
                    using var content = await graphClient.Me.Drive.Items[item.Id].Content.Request().GetAsync();
                    await content.CopyToAsync(new FileStream(System.IO.Path.Combine(config.LocalFolder, item.Name), FileMode.Create, FileAccess.Write));
                    System.Console.WriteLine("\tdownloaded.");
                    sock?.SendFrame($"{item.Name} downloaded.");
                }
            }
            catch (ServiceException ex)
            {
                Console.Error.WriteLine(ex.Message);
                sock?.SendFrame(ex.Message);
                context.ExitCode = 1;
            }
            sock?.SendFrame("done");
            if(sock is not null)
            {
                sock.Close();
                sock.Dispose();
            }
        });


        checkCommand.SetHandler(async (context) =>
        {
            var configFile = context.ParseResult.GetValueForOption(configFileOption);
            var config = ParseConfig(configFile);

            var app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithRedirectUri("http://localhost")
            .Build();

            var storageProperties =
                new StorageCreationPropertiesBuilder("DS3SaveBackup_cache.json", config.WorkingDirectory)
                    .WithUnprotectedFile()
                    .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);


            var accounts = await app.GetAccountsAsync();
            //var strAccounts = string.Join("\n\t", accounts.Select(x => x.Username));
            //Console.WriteLine($"Cached accounts:\n\t{strAccounts}");
            AuthenticationResult? result = null;
            //string? ErrorMessage = null;

            try
            {
                result = await app.AcquireTokenSilent(config.Scopes, accounts.FirstOrDefault()).ExecuteAsync();
                context.ExitCode = 0;
            }
            catch (MsalUiRequiredException ex)
            {
                Console.Error.Write($"No log in: {ex.Message}");
                context.ExitCode = 1;
            }

        });

        return await rootCommand.InvokeAsync(args);

    }

    private static DS3SaveBackupOptions ParseConfig(string? filename)
    {

        if (!System.IO.File.Exists(filename))
        {
            //result.ErrorMessage = "Config file does not exist";
            return new DS3SaveBackupOptions();
        }
        else
        {
            IConfiguration config = new ConfigurationBuilder()
                                        .AddJsonFile(filename)
                                        .Build();

            var backupOptions = new DS3SaveBackupOptions();
            backupOptions.WorkingDirectory = config["WorkingDirectory"] ?? backupOptions.WorkingDirectory;
            backupOptions.ClientId = config["ClientId"] ?? backupOptions.ClientId;
            backupOptions.CloudFolder = config["CloudFolder"] ?? backupOptions.CloudFolder;
            backupOptions.LocalFolder = config["LocalFolder"] ?? backupOptions.LocalFolder;
            backupOptions.Scopes = config.GetSection("Scopes").Get<string[]>() ?? backupOptions.Scopes;
            backupOptions.Socket = config["Socket"] ?? backupOptions.Socket;

            return backupOptions;
        }
    }



    private static SystemWebViewOptions GetCustomHTML(NetMQ.Sockets.RequestSocket? socket)
    {
        return new SystemWebViewOptions
        {
            HtmlMessageSuccess = @"<html style='font-family: sans-serif;'>
                                      <head><title>Authentication Complete</title></head>
                                      <body style='text-align: center;'>
                                          <header>
                                              <h1>Custom Web UI</h1>
                                          </header>
                                          <main style='border: 1px solid lightgrey; margin: auto; width: 600px; padding-bottom: 15px;'>
                                              <h2 style='color: limegreen;'>Authentication complete</h2>
                                              <div>You can return to the application. Feel free to close this browser tab.</div>
                                          </main>
    
                                      </body>
                                    </html>",

            HtmlMessageError = @"<html style='font-family: sans-serif;'>
                                  <head><title>Authentication Failed</title></head>
                                  <body style='text-align: center;'>
                                      <header>
                                          <h1>Custom Web UI</h1>
                                      </header>
                                      <main style='border: 1px solid lightgrey; margin: auto; width: 600px; padding-bottom: 15px;'>
                                          <h2 style='color: salmon;'>Authentication failed</h2>
                                          <div><b>Error details:</b> error {0} error_description: {1}</div>
                                          <br>
                                          <div>You can return to the application. Feel free to close this browser tab.</div>
                                      </main>
    
                                  </body>
                                </html>",

            OpenBrowserAsync = (uri) =>
            {

                System.Console.WriteLine($"Please open browser with uri to sign in: {uri}");

                socket?.SendFrame(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<AuthResult>(new AuthResult
                {
                    Ok = false,
                    LoginInfo = new AuthInfo
                    {
                        VerificationUrl = uri.ToString()
                    }
                }));
                return Task.CompletedTask;
            }
        };
    }
}