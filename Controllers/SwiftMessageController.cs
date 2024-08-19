using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;
using System.Data.SQLite;
using Microsoft.Extensions.Logging;
using System;

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

        // POST api/SwiftMessage
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
                return StatusCode(StatusCodes.Status500InternalServerError, "Error processing file.");
            }

            // Parse the Swift MT799 message
            var fields = ParseSwiftMessage(content);
            if (fields == null)
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
            try
            {
                SaveToDatabase(fields);
                _logger.LogInformation("File processed and data saved successfully.");
            }
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, $"SQLite error: {ex.ErrorCode} - {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "SQLite error occurred while saving data.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error occurred while saving data to the database.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error saving data.");
            }

            return Ok("File processed successfully.");
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
                return null;
            }

            _logger.LogInformation("Parsing completed.");
            return fields;
        }

        // Method to save parsed data to SQLite database
        private void SaveToDatabase(Dictionary<string, string> fields)
        {
            _logger.LogInformation("Saving data to database.");

            // Ensure the database path is correct
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "swift_messages.db");
            _logger.LogInformation($"SQLite Database Path: {dbPath}");

            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    connection.Open();

                    // Log successful database connection
                    _logger.LogInformation("Database connection opened successfully.");

                    // Create the table if it doesn't exist
                    using (var createTableCmd = new SQLiteCommand())
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
                    using (var command = new SQLiteCommand())
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
            }
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, $"SQLite error: {ex.ErrorCode} - {ex.Message}");
                throw;  // Re-throw the exception to ensure the error is propagated
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error occurred while saving data to the database.");
                throw;  // Re-throw the exception to ensure the error is propagated
            }
        }
    }
}
