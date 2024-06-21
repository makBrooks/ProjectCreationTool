using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

try
{
    // Check and install .NET Hosting Bundle
    await CheckAndInstallHostingBundle();
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}

app.Run();

static async Task CheckAndInstallHostingBundle()
{
    string url = "https://download.visualstudio.microsoft.com/download/pr/751d3fcd-72db-4da2-b8d0-709c19442225/33cc492bde704bfd6d70a2b9109005a0/dotnet-hosting-8.0.6-win.exe";
    string destinationPath = @"C:\temp\dotnet-hosting-8.0.6-win.exe";

    // Check if the hosting bundle is already installed
    if (IsHostingBundleInstalled())
    {
        Console.WriteLine("The .NET Hosting Bundle is already installed.");
        return;
    }

    // Download the file
    await DownloadFileAsync(url, destinationPath);

    // Install the new hosting bundle
    InstallHostingBundle(destinationPath);
}

static async Task DownloadFileAsync(string url, string destinationPath)
{
    try
    {
        using HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Set timeout to 10 minutes
        };

        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync();
        using FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        Console.WriteLine("Download completed.");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"HTTP error occurred during download: {ex.Message}");
        throw;
    }
    catch (IOException ex)
    {
        Console.WriteLine($"I/O error occurred during download: {ex.Message}");
        throw;
    }
    catch (TaskCanceledException ex)
    {
        Console.WriteLine($"The request was canceled due to timeout: {ex.Message}");
        throw;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An unexpected error occurred during download: {ex.Message}");
        throw;
    }
}

static void InstallHostingBundle(string installerPath)
{
    try
    {
        Process process = new Process();
        process.StartInfo.FileName = installerPath;
        process.StartInfo.Arguments = "/quiet";
        process.StartInfo.UseShellExecute = true;
        process.StartInfo.Verb = "runas";
        process.Start();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            Console.WriteLine("Installation completed successfully.");
        }
        else
        {
            Console.WriteLine($"Installation failed with exit code {process.ExitCode}.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred during installation: {ex.Message}");
        throw;
    }
}

static bool IsHostingBundleInstalled()
{
    try
    {
        // Check if the .NET Hosting Bundle is installed
        string registryKey = @"SOFTWARE\Microsoft\ASP.NET Core\Shared Framework\";
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKey);
        return key != null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while checking if the hosting bundle is installed: {ex.Message}");
        throw;
    }
}