using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

namespace SwiftMT799Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SwiftMessageController : ControllerBase
    {
        private readonly ILogger<SwiftMessageController> _logger;

        public SwiftMessageController(ILogger<SwiftMessageController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Uploads and processes a Swift MT799 message file.
        /// </summary>
        /// <param name="file">The Swift MT799 message file to be processed.</param>
        /// <returns>An IActionResult indicating the success or failure of the operation.</returns>
        [HttpPost]
        public IActionResult UploadSwiftFile(IFormFile file)
        {
            _logger.LogInformation("UploadSwiftFile called");

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file uploaded.");
                return BadRequest("No file uploaded.");
            }

            // Read file content
            string content;
            try
            {
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    content = reader.ReadToEnd();
                }
                _logger.LogInformation("File content read successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file content.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error processing file: " + ex.Message);
            }

            // Parse the Swift MT799 message
            var fields = ParseSwiftMessage(content);
            if (fields.Count == 0)
            {
                _logger.LogWarning("Failed to parse Swift message.");
                return BadRequest("Invalid Swift MT799 message format.");
            }

            // Log parsed fields for debugging
            foreach (var field in fields)
            {
                _logger.LogInformation($"Field {field.Key}: {field.Value}");
            }

            // Save parsed data to the database
            bool savedToDatabase = false;
            try
            {
                savedToDatabase = SaveToDatabase(fields);
                _logger.LogInformation("File processed and data saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while saving data to the database.");
                // Note: We're not returning an error here, as we still want to show the parsed fields
            }

            // Prepare the response
            var response = new SwiftMessageResponse
            {
                Message = "File processed successfully.",
                ParsedFields = fields,
                SavedToDatabase = savedToDatabase
            };

            return Ok(response);
        }

        // Method to parse the Swift MT799 message
        private Dictionary<string, string> ParseSwiftMessage(string content)
        {
            _logger.LogInformation("Parsing Swift MT799 message.");
            var fields = new Dictionary<string, string>();

            string[] lines = content.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (line.StartsWith(":20:"))
                    fields["Reference"] = line.Substring(4).Trim();
                else if (line.StartsWith(":21:"))
                    fields["RelatedReference"] = line.Substring(4).Trim();
                else if (line.StartsWith(":79:"))
                    fields["Narrative"] = line.Substring(4).Trim();
            }

            // Check if all required fields are present
            if (!fields.ContainsKey("Reference") || !fields.ContainsKey("RelatedReference") || !fields.ContainsKey("Narrative"))
            {
                _logger.LogWarning("Missing required fields in the Swift message.");
                return new Dictionary<string, string>();
            }

            _logger.LogInformation("Parsing completed.");
            return fields;
        }

        // Method to save parsed data to SQLite database
        private bool SaveToDatabase(Dictionary<string, string> fields)
        {
            _logger.LogInformation("Saving data to database.");

            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "swift_messages.db");
            _logger.LogInformation($"SQLite Database Path: {dbPath}");

            // Ensure the directory exists
            string? directoryPath = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            try
            {
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();

                    // Log successful database connection
                    _logger.LogInformation("Database connection opened successfully.");

                    // Create the table if it doesn't exist
                    using (var createTableCmd = new SqliteCommand())
                    {
                        createTableCmd.Connection = connection;
                        createTableCmd.CommandText = @"CREATE TABLE IF NOT EXISTS SwiftMessages (
                                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                                        Reference TEXT,
                                                        RelatedReference TEXT,
                                                        Narrative TEXT
                                                    );";
                        createTableCmd.ExecuteNonQuery();
                        _logger.LogInformation("Table created or already exists.");
                    }

                    // Insert parsed data into the table
                    using (var command = new SqliteCommand())
                    {
                        command.Connection = connection;
                        command.CommandText = @"INSERT INTO SwiftMessages (Reference, RelatedReference, Narrative) 
                                                VALUES (@Reference, @RelatedReference, @Narrative)";
                        command.Parameters.AddWithValue("@Reference", fields["Reference"]);
                        command.Parameters.AddWithValue("@RelatedReference", fields["RelatedReference"]);
                        command.Parameters.AddWithValue("@Narrative", fields["Narrative"]);

                        command.ExecuteNonQuery();
                        _logger.LogInformation("Data saved to database successfully.");
                    }
                }
                return true; // Return true if save was successful
            }
            catch (SqliteException ex)
            {
                _logger.LogError(ex, $"SQLite error: {ex.ErrorCode} - {ex.Message}");
                return false; // Return false if save failed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error occurred while saving data to the database.");
                return false; // Return false if save failed
            }
        }
    }

    public class SwiftMessageResponse
    {
        public string Message { get; set; }
        public Dictionary<string, string> ParsedFields { get; set; }
        public bool SavedToDatabase { get; set; }
    }
}