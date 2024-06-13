
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;
using ProjectCreator.Models;
using System.ComponentModel.Design;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ProjectCreator.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }


        // Method to generate a project and add properties from an Excel file to model classes
        [HttpPost]
        public async Task<IActionResult> GenerateProject([FromForm] CreatorEntity input)
        {
            if (string.IsNullOrWhiteSpace(input.ProjectName) || input.ModelNames.Length == 0)
            {
                return BadRequest("Invalid input");
            }

            if (input.ExcelFile == null || input.ExcelFile.Length == 0)
            {
                return BadRequest("Excel file is required.");
            }

            string projectDirectory = Path.Combine("D:\\", input.ProjectName);

            if (Directory.Exists(projectDirectory))
            {
                try
                {
                    Directory.Delete(projectDirectory, true);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Failed to delete existing project directory: {ex.Message}");
                }
            }

            try
            {
                Directory.CreateDirectory(projectDirectory);
                string dbcontext = Path.Combine(projectDirectory, "DBContexts");
                Directory.CreateDirectory(dbcontext);
                string solutionPath = Path.Combine(projectDirectory, $"{input.ProjectName}.sln");
                bool isDotnetEfInstalled = await IsDotnetEfInstalledAsync();

                if (isDotnetEfInstalled)
                {
                    ExecuteDotnetCommand("dotnet tool update --global dotnet-ef");
                }
                else
                {
                    ExecuteDotnetCommand("dotnet tool install --global dotnet-ef");
                }
                ExecuteDotnetCommand($"new sln -n {input.ProjectName} -o \"{projectDirectory}\"");

                ExecuteDotnetCommand($"new mvc -o \"{projectDirectory}\"");
                // Install Microsoft.EntityFrameworkCore package

                ExecuteDotnetCommand($"dotnet add {Path.Combine(projectDirectory, $"{input.ProjectName}.csproj")} package Microsoft.EntityFrameworkCore");
                ExecuteDotnetCommand($"dotnet add {Path.Combine(projectDirectory, $"{input.ProjectName}.csproj")} package Microsoft.EntityFrameworkCore.SqlServer");
                ExecuteDotnetCommand($"dotnet add {Path.Combine(projectDirectory, $"{input.ProjectName}.csproj")} package Microsoft.EntityFrameworkCore.Tools");


                ExecuteDotnetCommand($"sln \"{solutionPath}\" add \"{Path.Combine(projectDirectory, $"{input.ProjectName}.csproj")}\"");

                string modelsPath = Path.Combine(projectDirectory, "Models");
                Directory.CreateDirectory(modelsPath);
                foreach (string modelName in input.ModelNames)
                {
                    string modelFilePath = Path.Combine(modelsPath, $"{modelName.Trim()}.cs");
                    await System.IO.File.WriteAllTextAsync(modelFilePath, GenerateModelClass(input.ProjectName, modelName, new List<(string, string)>()));
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                await AddPropertiesFromExcel(input.ExcelFile, modelsPath, input);
                // Create DbContext file and add connection string to appsettings.json
                await GenerateDbContext(projectDirectory, input);

                // Call method to add connection string to appsettings.json
                await AddConnectionStringToAppSettings(projectDirectory, input);


                string repositoriesPath = Path.Combine(projectDirectory, "Repositories");
                Directory.CreateDirectory(repositoriesPath);
                await GenerateRepositories(projectDirectory, input);

                // Generate and write the repository container
                string containerDirectory = Path.Combine(projectDirectory, "Container");
                Directory.CreateDirectory(containerDirectory);
                string repositoryContainerContent = GenerateRepositoryContainer(input);
                string repositoryContainerPath = Path.Combine(containerDirectory, "CustomContainer.cs");
                await System.IO.File.WriteAllTextAsync(repositoryContainerPath, repositoryContainerContent);
                // Generate MVC controllers for each model

                // Parse the Excel file to get model properties
                //var modelProperties = await ParseExcelFile(input.ExcelFile);
                await GenerateMvcControllers(projectDirectory, input.ModelNames, input.ProjectName);

                // Generate views for each model
                foreach (string modelName in input.ModelNames)
                {
                    var propertyNames = headers
            .Where(header => header.ModelName == modelName)
            .Select(header => header.PropertyName)
            .ToList();
                    await GenerateViews(projectDirectory, input.ProjectName, modelName, propertyNames);
                }

                // method after generating the project to create database
                await RunMigrationCommandAndOpenSolution(projectDirectory, "InitialMigration", solutionPath);


                // Run migration script in Package Manager Console
                //await RunMigrationScript(scriptPath);


                //// Open the solution in Visual Studio
                //OpenSolutionInVisualStudio(solutionPath);
                return Ok(new { message = "Project created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to create project: {ex.Message}");
            }
        }

        private async Task<bool> IsDotnetEfInstalledAsync()
        {
            string result = await ExecuteDotnetCommandAsync("dotnet tool list --global");
            return result.Contains("dotnet-ef");
        }

        private async Task<string> ExecuteDotnetCommandAsync(string command)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return output.ToString();
        }
        private async Task RunMigrationCommandAndOpenSolution(string projectDirectory, string migrationName, string solutionPath)
        {
            try
            {
                // Commands to be executed
                string addMigrationCommand = $"dotnet ef migrations add {migrationName}";
                string updateDatabaseCommand = "dotnet ef database update";

                // Combine the commands with the necessary parameters for cmd.exe
                string arguments = $"/c cd \"{projectDirectory}\" && {addMigrationCommand} && {updateDatabaseCommand}";

                // Start a new process to run the commands in the command prompt
                await Task.Run(() =>
                {
                    ProcessStartInfo processStartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(processStartInfo))
                    {
                        // Capture the standard output and error (if needed)
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        process.WaitForExit();

                        // Output the results to the console (or handle as needed)
                        if (!string.IsNullOrEmpty(output))
                        {
                            Console.WriteLine(output);
                        }

                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine(error);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions
                Console.WriteLine($"Failed to run migration commands and open solution in Visual Studio: {ex.Message}");
            }
        }





        List<(string ModelName, string PropertyName, string DataType, bool IsPrimaryKey)> headers = new List<(string ModelName, string PropertyName, string DataType, bool IsPrimaryKey)>();


        public async Task AddPropertiesFromExcel(IFormFile excelFile, string modelsPath, CreatorEntity input)
        {
            try
            {
                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;
                var IsPrimaryKey = false;
                //var headers = new List<(string ModelName, string PropertyName, string DataType, bool IsPrimaryKey)>();

                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();

                if (worksheet != null)
                {
                    for (var row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        var modelName = worksheet.Cells[row, 1].Text.Trim();
                        var propertyName = worksheet.Cells[row, 2].Text.Trim();
                        var dataType = worksheet.Cells[row, 3].Text.Trim();
                        var primaryKey = worksheet.Cells[row, 4].Text.Trim();
                        if (primaryKey.ToLower()=="primary key" || primaryKey.ToLower() == "primarykey" || primaryKey.ToLower() == "primary")
                        {
                            IsPrimaryKey = true;
                        }
                        if (input.ModelNames.Contains(modelName) && !string.IsNullOrWhiteSpace(propertyName) && !string.IsNullOrWhiteSpace(dataType))
                        {
                            headers.Add((modelName, propertyName, GetCSharpType(dataType), IsPrimaryKey));
                        }
                        IsPrimaryKey = false;
                    }
                }

                foreach (var modelName in input.ModelNames)
                {
                    var modelHeaders = headers
                        .Where(h => h.ModelName == modelName)
                        .Select(h => (h.PropertyName, h.DataType, h.IsPrimaryKey))
                        .ToList();

                    if (modelHeaders.Count == 0)
                    {
                        //modelHeaders.Add(("PlaceholderProperty", "string", false));
                    }

                    var modelFilePath = Path.Combine(modelsPath, $"{modelName}.cs");
                    await System.IO.File.WriteAllTextAsync(modelFilePath, GenerateModelClass(input.ProjectName, modelName, modelHeaders));
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error processing Excel file: {ex.Message}", ex);
            }
        }

        private string GenerateModelClass(string projectName, string modelName, List<(string PropertyName, string DataType, bool IsPrimaryKey)> headers)
        {
            var classCode = $"// Generated class for {modelName}\n";
            classCode = $"using System.ComponentModel.DataAnnotations;\r\nusing System.ComponentModel.DataAnnotations.Schema;\n";
            classCode += $"namespace {projectName}.Models\n{{\n";
            classCode += $"    public class {modelName}\n    {{\n";

            foreach (var (propertyName, dataType, isPrimaryKey) in headers)
            {
                if (isPrimaryKey)
                {
                    classCode += "        [Key]\n";
                }
                if (dataType=="int")
                {
                    classCode += $"        public {dataType} {propertyName} {{ get; set; }} = 0;\n";
                }
                else
                {
                    classCode += $"        public {dataType} {propertyName} {{ get; set; }} = null;\n";
                }
                
            }

            classCode += "    }\n}";
            return classCode;
        }

    // Method to execute command
    private void ExecuteDotnetCommand(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo("dotnet", command)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = new Process { StartInfo = processInfo };
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    throw new Exception($"Command '{command}' failed: {error}");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        // Method to convert Property Datatype
        private string GetCSharpType(string dataType)
        {
            try
            {
                return dataType.ToLower() switch
                {
                    "int" => "int",
                    "string" => "string",
                    _ => "string"
                };
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }
        // Method to generate a model class
        private string GenerateModelClass(string projectName, string modelName, List<(string PropertyName, string DataType)> properties)
        {
            try
            {
                var propertyLines = properties.Select(p =>
                        $"public {GetCSharpType(p.DataType)} {p.PropertyName} {{ get; set; }} = {(GetCSharpType(p.DataType) == "int" ? "0" : "\"\"")};").ToList();

                return $@"
using System;
using System.Collections.Generic;

namespace {projectName}.Models
{{
    public class {modelName}
    {{
        {string.Join(Environment.NewLine, propertyLines)}
    }}
}}";
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        // Method to generate db context class
        private async Task GenerateDbContext(string projectDirectory, CreatorEntity input)
        {
            try
            {
                // Create DbContext file
                string dbContextFilePath = Path.Combine(projectDirectory, "DBContexts", $"{input.ProjectName}DbContext.cs");
                string dbContextContent = GenerateDbContextClass(input.ProjectName, input.ModelNames);
                await System.IO.File.WriteAllTextAsync(dbContextFilePath, dbContextContent);

            }
            catch (Exception ex)
            {

                throw ex;
            }
        }
        
        private string GenerateDbContextClass(string projectName, string[] modelNames)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("using Microsoft.EntityFrameworkCore;");
                sb.AppendLine("using System;");
                sb.AppendLine($"using {projectName}.Models;");
                sb.AppendLine();
                sb.AppendLine($"namespace {projectName}.DBContexts");
                sb.AppendLine("{");
                sb.AppendLine($"    public class {projectName}DbContext : DbContext");
                sb.AppendLine("    {");
                sb.AppendLine($"        public {projectName}DbContext(DbContextOptions<{projectName}DbContext> options) : base(options)");
                sb.AppendLine("        {");
                sb.AppendLine("        }");
                sb.AppendLine();

                // Generate DbSet properties for each model
                foreach (string modelName in modelNames)
                {
                    sb.AppendLine($"" +
                        $"        public DbSet<{modelName.Trim()}> {modelName.Trim()}DbSet {{ get; set; }}");
                }

                sb.AppendLine("    }");
                sb.AppendLine("}");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        // Method to generate connectionstring in Appsetting.json
        private async Task AddConnectionStringToAppSettings(string projectDirectory, CreatorEntity input)
        {
            try
            {
                // Read the content of appsettings.json
                string appsettingsPath = Path.Combine(projectDirectory, "appsettings.json");
                string appsettingsJson = await System.IO.File.ReadAllTextAsync(appsettingsPath);

                // Deserialize the JSON content
                dynamic appsettings = JsonConvert.DeserializeObject(appsettingsJson);

                // Add or update the connection string
                // Check if appsettings object is not null before accessing properties
                if (appsettings != null)
                {
                    // Check if ConnectionStrings property exists, create it if it doesn't
                    if (appsettings["ConnectionStrings"] == null)
                    {
                        // Create ConnectionStrings property as an empty object
                        appsettings["ConnectionStrings"] = new JObject();
                    }

                    // Set the DefaultConnection property
                    appsettings["ConnectionStrings"]["DefaultConnection"] = $"Data Source={input.ServerName};Initial Catalog={input.DatabaseName};User Id={input.ID};Password={input.Password};TrustServerCertificate=True; Integrated Security=False;Persist Security Info=False;";
                    var connectionString= $"Data Source={input.ServerName};Initial Catalog={input.DatabaseName};User Id={input.ID};Password={input.Password};TrustServerCertificate=True; Integrated Security=False;Persist Security Info=False;";
                    // Write the updated appsettings back to the file
                    await System.IO.File.WriteAllTextAsync(appsettingsPath, JsonConvert.SerializeObject(appsettings, Newtonsoft.Json.Formatting.Indented));
                    ConfigureDbContext(projectDirectory, input);
                }
                else
                {
                    // Serialize the modified JSON content
                    string updatedAppsettingsJson = JsonConvert.SerializeObject(appsettings, Newtonsoft.Json.Formatting.Indented);

                    // Write the updated content back to appsettings.json
                    await System.IO.File.WriteAllTextAsync(appsettingsPath, updatedAppsettingsJson);
                }

                
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }
        // Method to configure db context in program.cs
        public async void ConfigureDbContext(string projectDirectory, CreatorEntity input)
        {
            try
            {
                string programPath = Path.Combine(projectDirectory, "Program.cs");
                string programData = await System.IO.File.ReadAllTextAsync(programPath);
                // Find the index of "// Add services to the container."
                int index = programData.IndexOf("// Add services to the container.");
                if (index != -1)
                {
                    string insertnamespace = $"using {input.ProjectName}.DBContexts;\r\nusing {input.ProjectName}.Container;\r\nusing Microsoft.EntityFrameworkCore;\r\n";
                    // Insert your string below the "// Add services to the container."
                    string insertString = $"// Configure DbContext with SQL Server\r\nbuilder.Services.AddDbContext<{input.ProjectName}DbContext>(options =>\r\n    options.UseSqlServer(\r\n        builder.Configuration.GetConnectionString(\"DefaultConnection\")));\r\nbuilder.Services.AddCustomContainer(builder.Configuration);\n";
                    programData = programData.Insert(index + "// Add services to the container.\n".Length, insertString);
                    programData = programData.Insert(0, insertnamespace);
                }
                await System.IO.File.WriteAllTextAsync(programPath, programData);
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }
        //Method to generate Repositories
        private async Task GenerateRepositories(string projectDirectory, CreatorEntity input)
        {
            try
            {
                string repositoriesPath = Path.Combine(projectDirectory, "Repositories");
                Directory.CreateDirectory(repositoriesPath);

                foreach (var modelName in input.ModelNames)
                {
                    // Generate interface
                    string interfaceContent = GenerateInterfaceContent(input, modelName);
                    string interfaceFilePath = Path.Combine(repositoriesPath, $"I{modelName}Repository.cs");
                    await System.IO.File.WriteAllTextAsync(interfaceFilePath, interfaceContent);

                    // Generate repository class
                    string repositoryContent = GenerateRepositoryContent(input, modelName);
                    string repositoryFilePath = Path.Combine(repositoriesPath, $"{modelName}Repository.cs");
                    await System.IO.File.WriteAllTextAsync(repositoryFilePath, repositoryContent);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        //Method to generate Interface content
        private string GenerateInterfaceContent(CreatorEntity input,string modelName)
        {
            try
            {
                return $@"
using System.Collections.Generic;
using System.Threading.Tasks;
using {input.ProjectName}.Models;

namespace {input.ProjectName}.Repositories
{{
    public interface I{modelName}Repository
    {{
        Task<List<{modelName}>> GetAllAsync();
        Task<{modelName}> GetByIdAsync(int id);
        Task AddAsync({modelName} entity);
        Task UpdateAsync({modelName} entity);
        Task DeleteAsync(int id);
    }}
}}";
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        //generate repository content
        private string GenerateRepositoryContent(CreatorEntity input,string modelName)
        {
            try
            {
                return $@"
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using {input.ProjectName}.DBContexts;
using {input.ProjectName}.Models;

namespace {input.ProjectName}.Repositories
{{
    public class {modelName}Repository : I{modelName}Repository
    {{
        private readonly {input.ProjectName}DbContext _context;

        public {modelName}Repository({input.ProjectName}DbContext context)
        {{
            _context = context;
        }}

        public async Task<List<{modelName}>> GetAllAsync()
        {{
            try
            {{
                return await _context.{modelName}DbSet.ToListAsync();
            }}
            catch (Exception ex)
            {{
                throw ex;
            }}
        }}

        public async Task<{modelName}> GetByIdAsync(int id)
        {{
            try
            {{
                return await _context.{modelName}DbSet.FindAsync(id);
            }}
            catch (Exception ex)
            {{
                throw ex;
            }}            
        }}

        public async Task AddAsync({modelName} entity)
        {{
            try
            {{
                await _context.{modelName}DbSet.AddAsync(entity);
                await _context.SaveChangesAsync();
            }}
            catch (Exception ex)
            {{
                throw ex;
            }}            
        }}

        public async Task UpdateAsync({modelName} entity)
        {{
            try
            {{
                 _context.{modelName}DbSet.Update(entity);
                await _context.SaveChangesAsync();
            }}
            catch (Exception ex)
            {{
                throw ex;
            }}           
        }}

        public async Task DeleteAsync(int id)
        {{
            try
            {{
                var entity = await _context.{modelName}DbSet.FindAsync(id);
                if (entity != null)
                {{
                    _context.{modelName}DbSet.Remove(entity);
                    await _context.SaveChangesAsync();
                }}
            }}
            catch (Exception ex)
            {{
                throw ex;
            }}            
        }}


    }}
}}";
                //public async Task<bool> ExistsAsync(int id)
                //{{
                //    try
                //    {{
                //        return await _context.{modelName}DbSet.AnyAsync(e => e.id == id);
                //    }}
                //    catch (Exception ex)
                //    {{
                //        throw ex;
                //    }}
                //}}
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        //Generate  Custom Container
        private string GenerateRepositoryContainer(CreatorEntity input)
        {
            try
            {
                var repositoriesRegistration = new System.Text.StringBuilder();
                foreach (var modelName in input.ModelNames)
                {
                    repositoriesRegistration.AppendLine($@"         services.AddScoped<I{modelName}Repository, {modelName}Repository>();");
                }

                return $@"
using Microsoft.Extensions.DependencyInjection;
using {input.ProjectName}.Repositories;
namespace {input.ProjectName}.Container
{{
    public static class CustomContainer
    {{
        public static void AddCustomContainer(this IServiceCollection services,IConfiguration configuration)
        {{
            {repositoriesRegistration.ToString().TrimEnd()}
        }}
    }}
}}";
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        //Generate Controller Content
        private async Task GenerateMvcControllers(string projectDirectory, string[] modelNames, string namespaceName)
        {
            string controllersPath = Path.Combine(projectDirectory, "Controllers");
            Directory.CreateDirectory(controllersPath);

            foreach (var modelName in modelNames)
            {
                //var properties = modelProperties.ContainsKey(modelName) ? modelProperties[modelName] : new List<string> { "Id" };
                string controllerContent = GenerateControllerTemplate(namespaceName, modelName);
                string controllerFilePath = Path.Combine(controllersPath, $"{modelName}Controller.cs");
                await System.IO.File.WriteAllTextAsync(controllerFilePath, controllerContent);
            }
        }


        private string GenerateControllerTemplate(string namespaceName, string modelName)
        {

            return $@"
using Microsoft.AspNetCore.Mvc;
using {namespaceName}.Models;
using {namespaceName}.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace {namespaceName}.Controllers
{{
    public class {modelName}Controller : Controller
    {{
        private readonly I{modelName}Repository _{modelName.ToLower()}Repository;

        public {modelName}Controller(I{modelName}Repository {modelName.ToLower()}Repository)
        {{
            _{modelName.ToLower()}Repository = {modelName.ToLower()}Repository;
        }}

        // GET: {modelName}
        public async Task<IActionResult> Index()
        {{
            try
            {{
                var {modelName.ToLower()}List = await _{modelName.ToLower()}Repository.GetAllAsync();
                return View({modelName.ToLower()}List);
            }}
            catch (Exception ex)
            {{
                return StatusCode(500, $""Internal server error: {{ex.Message}}"");
            }}
        }}

        // GET: {modelName}/Details/5
        public async Task<IActionResult> {modelName}Details(int? id)
        {{
            if (id == null)
            {{
                return NotFound();
            }}

            try
            {{
                var {modelName.ToLower()} = await _{modelName.ToLower()}Repository.GetByIdAsync(id.Value);
                if ({modelName.ToLower()} == null)
                {{
                    return NotFound();
                }}

                return View({modelName.ToLower()});
            }}
            catch (Exception ex)
            {{
                return StatusCode(500, $""Internal server error: {{ex.Message}}"");
            }}
        }}

        // GET: {modelName}/Create
        public IActionResult {modelName}Create()
        {{
            return View();
        }}

        // POST: {modelName}/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> {modelName}Create({modelName} model)
        {{
            if (ModelState.IsValid)
            {{
                try
                {{
                    await _{modelName.ToLower()}Repository.AddAsync(model);
                    return RedirectToAction(nameof(Index));
                }}
                catch (Exception ex)
                {{
                    return StatusCode(500, $""Internal server error: {{ex.Message}}"");
                }}
            }}
            return View(model);
        }}

        // GET: {modelName}/Edit/5
        public async Task<IActionResult> {modelName}Edit(int? id)
        {{
            if (id == null)
            {{
                return NotFound();
            }}

            try
            {{
                var model = await _{modelName.ToLower()}Repository.GetByIdAsync(id.Value);
                if (model == null)
                {{
                    return NotFound();
                }}
                return View(model);
            }}
            catch (Exception ex)
            {{
                return StatusCode(500, $""Internal server error: {{ex.Message}}"");
            }}
        }}

        // POST: {modelName}/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> {modelName}Edit({modelName} model)
        {{
            if (ModelState.IsValid)
            {{
                try
                {{
                    await _{modelName.ToLower()}Repository.UpdateAsync(model);
                    return RedirectToAction(nameof(Index));
                }}                
                catch (Exception ex)
                {{
                    return StatusCode(500, $""Internal server error: {{ex.Message}}"");
                }}
            }}
            return View(model);
        }}

        // GET: {modelName}/Delete/5
        public async Task<IActionResult> {modelName}Delete(int? id)
        {{
            if (id == null)
            {{
                return NotFound();
            }}

            try
            {{
                var model = await _{modelName.ToLower()}Repository.GetByIdAsync(id.Value);
                if (model == null)
                {{
                    return NotFound();
                }}
                return View(model);
            }}
            catch (Exception ex)
            {{
                return StatusCode(500, $""Internal server error: {{ex.Message}}"");
            }}
        }}

        // POST: {modelName}/Delete/5
        [HttpPost, ActionName(""Delete"")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {{
            try
            {{
                await _{modelName.ToLower()}Repository.DeleteAsync(id);
                return RedirectToAction(nameof(Index));
            }}
            catch (Exception ex)
            {{
                return StatusCode(500, $""Internal server error: {{ ex.Message}}"");
            }}
        }}        
    }}
}}
";
            
        }



        private async Task GenerateViews(string projectDirectory, string namespaceName, string modelName, List<string> properties)
        {
            try
            {
                string viewsFolder = Path.Combine(projectDirectory, "Views");

                string controllerFolder = Path.Combine(viewsFolder, modelName);
                Directory.CreateDirectory(controllerFolder);

                // Generate combined views
                string addEditViewContent = GenerateAddEditViewTemplate(namespaceName, modelName, properties);
                string viewDeleteViewContent = GenerateViewDeleteViewTemplate(namespaceName, modelName, properties);

                string addEditViewFilePath = Path.Combine(controllerFolder,  modelName + "Create.cshtml");
                string viewDeleteViewFilePath = Path.Combine(controllerFolder, modelName + "Details.cshtml");

                await System.IO.File.WriteAllTextAsync(addEditViewFilePath, addEditViewContent);
                await System.IO.File.WriteAllTextAsync(viewDeleteViewFilePath, viewDeleteViewContent);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to generate views: {ex.Message}");
            }
        }

        private string GenerateAddEditViewTemplate(string namespaceName, string modelName, List<string> properties)
        {
            string formFields = string.Join("\n", properties.Select(p => $@"
<div class=""form-group"">
    <label asp-for=""{p}"" class=""control-label""></label>
    <input asp-for=""{p}"" class=""form-control"" />
    <span asp-validation-for=""{p}"" class=""text-danger""></span>
</div>"));

            return $@"
@model {namespaceName}.Models.{modelName}

@{{
    ViewData[""Title""] = ""Add {modelName}"";
}}

<h1>@ViewData[""Title""]</h1>

<h4>{modelName}</h4>
<hr />
<div class=""row"">
    <div class=""col-md-4"">
        <form asp-action=""{modelName}Create"">
            <div asp-validation-summary=""ModelOnly"" class=""text-danger""></div>
            {formFields}
            <div class=""form-group"">
                <input type=""submit"" value=""{modelName}Create"" class=""btn btn-primary"" />
            </div>
        </form>
    </div>
</div>

<div>
    <a asp-action=""Index"">Back to List</a>
</div>
";
        }

        private string GenerateViewDeleteViewTemplate(string namespaceName, string modelName, List<string> properties)
        {
            // Find the first string that contains "id"
            string firstPropertyWithId = properties.FirstOrDefault(property => property.Contains("id"));
            string detailFields = string.Join("\n", properties.Select(p => $@"
<dt class=""col-sm-2"">
    @Html.DisplayNameFor(model => model.{p})
</dt>
<dd class=""col-sm-10"">
    @Html.DisplayFor(model => model.{p})
</dd>"));

            return $@"
@model {namespaceName}.Models.{modelName}

@{{
    ViewData[""Title""] = ""View {modelName}"";
}}

<h1>@ViewData[""Title""]</h1>

<div>
    <h4>{modelName}</h4>
    <hr />
    <dl class=""row"">
        {detailFields}
    </dl>
</div>
<div>
    <a asp-action=""{modelName}Edit"" asp-route-id=""@Model.{firstPropertyWithId}"">Edit</a> |
    <a asp-action=""Index"">Back to List</a>
</div>

<form asp-action=""{modelName}Delete"">
    <input type=""hidden"" asp-for=""@Model.{firstPropertyWithId}"" />
    <input type=""submit"" value=""Delete"" class=""btn btn-danger"" />
</form>
";
        }








        public IActionResult AddProperties()
        {
            return View();
        }


        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
