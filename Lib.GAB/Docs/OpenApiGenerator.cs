using System.Collections.Generic;
using System.Linq;
using Lib.GAB.Tools;
using Newtonsoft.Json;

namespace Lib.GAB.Docs
{
    /// <summary>
    /// Generates an OpenAPI 3.0 specification from registered GABP tools.
    /// Each tool becomes a POST endpoint at /tools/{toolName}.
    /// </summary>
    public static class OpenApiGenerator
    {
        /// <summary>
        /// Generate an OpenAPI 3.0 JSON specification from the given tools
        /// </summary>
        public static string Generate(IList<ToolInfo> tools, string title, string version)
        {
            var spec = new Dictionary<string, object>
            {
                ["openapi"] = "3.0.3",
                ["info"] = new Dictionary<string, object>
                {
                    ["title"] = title,
                    ["version"] = version,
                    ["description"] = $"Auto-generated API documentation for {title} GABP tools. " +
                        "Tools are invoked via the GABP protocol (JSON-RPC over TCP), not HTTP. " +
                        "This spec is for reference only."
                },
                ["paths"] = BuildPaths(tools),
                ["components"] = new Dictionary<string, object>
                {
                    ["schemas"] = BuildSchemas(tools)
                }
            };

            return JsonConvert.SerializeObject(spec, Formatting.Indented);
        }

        private static Dictionary<string, object> BuildPaths(IList<ToolInfo> tools)
        {
            var paths = new Dictionary<string, object>();

            // Group tools by namespace prefix (e.g., "bannerlord.party", "bannerlord.ui")
            foreach (var tool in tools.OrderBy(t => t.Name))
            {
                var pathKey = $"/tools/{tool.Name}";
                var tag = GetTag(tool.Name);

                var operation = new Dictionary<string, object>
                {
                    ["summary"] = tool.Description ?? tool.Name,
                    ["operationId"] = tool.Name,
                    ["tags"] = new[] { tag },
                };

                // Request body
                if (tool.Parameters.Count > 0)
                {
                    operation["requestBody"] = new Dictionary<string, object>
                    {
                        ["required"] = true,
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = new Dictionary<string, object>
                                {
                                    ["$ref"] = $"#/components/schemas/{SchemaName(tool.Name)}_Request"
                                }
                            }
                        }
                    };
                }

                // Response
                var responseDesc = new Dictionary<string, object>
                {
                    ["description"] = "Tool result"
                };

                if (tool.ResponseFields.Count > 0)
                {
                    responseDesc["content"] = new Dictionary<string, object>
                    {
                        ["application/json"] = new Dictionary<string, object>
                        {
                            ["schema"] = new Dictionary<string, object>
                            {
                                ["$ref"] = $"#/components/schemas/{SchemaName(tool.Name)}_Response"
                            }
                        }
                    };
                }

                operation["responses"] = new Dictionary<string, object>
                {
                    ["200"] = responseDesc
                };

                paths[pathKey] = new Dictionary<string, object>
                {
                    ["post"] = operation
                };
            }

            return paths;
        }

        private static Dictionary<string, object> BuildSchemas(IList<ToolInfo> tools)
        {
            var schemas = new Dictionary<string, object>();

            foreach (var tool in tools)
            {
                // Request schema
                if (tool.Parameters.Count > 0)
                {
                    var properties = new Dictionary<string, object>();
                    var required = new List<string>();

                    foreach (var param in tool.Parameters)
                    {
                        var prop = new Dictionary<string, object>
                        {
                            ["type"] = MapClrTypeToJsonSchema(param.Type?.Name ?? "String")
                        };
                        if (!string.IsNullOrEmpty(param.Description))
                            prop["description"] = param.Description;
                        if (param.DefaultValue != null)
                            prop["default"] = param.DefaultValue;

                        properties[param.Name] = prop;

                        if (param.Required)
                            required.Add(param.Name);
                    }

                    var requestSchema = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                    };
                    if (required.Count > 0)
                        requestSchema["required"] = required;

                    schemas[$"{SchemaName(tool.Name)}_Request"] = requestSchema;
                }

                // Response schema
                if (tool.ResponseFields.Count > 0)
                {
                    var properties = new Dictionary<string, object>();
                    var required = new List<string>();

                    foreach (var field in tool.ResponseFields)
                    {
                        var prop = new Dictionary<string, object>
                        {
                            ["type"] = field.Type
                        };
                        if (!string.IsNullOrEmpty(field.Description))
                            prop["description"] = field.Description;
                        if (field.Nullable)
                            prop["nullable"] = true;

                        properties[field.Name] = prop;

                        if (field.Always)
                            required.Add(field.Name);
                    }

                    var responseSchema = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                    };
                    if (required.Count > 0)
                        responseSchema["required"] = required;

                    schemas[$"{SchemaName(tool.Name)}_Response"] = responseSchema;
                }
            }

            return schemas;
        }

        private static string GetTag(string toolName)
        {
            // "bannerlord.party/move_to_settlement" → "bannerlord.party"
            var slashIndex = toolName.IndexOf('/');
            return slashIndex > 0 ? toolName.Substring(0, slashIndex) : "general";
        }

        private static string SchemaName(string toolName)
        {
            // "bannerlord.party/move_to_settlement" → "bannerlord_party_move_to_settlement"
            return toolName.Replace('.', '_').Replace('/', '_');
        }

        private static string MapClrTypeToJsonSchema(string typeName)
        {
            switch (typeName)
            {
                case "String": return "string";
                case "Int32":
                case "Int64": return "integer";
                case "Single":
                case "Double": return "number";
                case "Boolean": return "boolean";
                default: return "string";
            }
        }
    }
}
