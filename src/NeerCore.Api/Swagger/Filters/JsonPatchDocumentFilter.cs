﻿using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using NeerCore.Api.Swagger.Internal;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.SwaggerGen;
using OperationType = Microsoft.AspNetCore.JsonPatch.Operations.OperationType;

namespace NeerCore.Api.Swagger.Filters;

/// <summary>
///   Swagger document filter to hide redundant models from
///   <see cref="Microsoft.AspNetCore.JsonPatch.JsonPatchDocument{TModel}"/>.
/// </summary>
public sealed class JsonPatchDocumentFilter : IDocumentFilter
{
    /// <inheritdoc cref="IDocumentFilter.Apply"/>
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Remove default JsonPatchDocument schemas
        RemoveContractResolverFromSchema(swaggerDoc);

        // Add correct 'Operation' schema instead of default
        FixOperationSchemas(swaggerDoc);

        // Apply correct '*JsonPatchDocument' schemas
        FixJsonPatchDocumentSchemas(swaggerDoc);
    }

    private static void RemoveContractResolverFromSchema(OpenApiDocument swaggerDoc)
    {
        if (swaggerDoc.Components.Schemas.ContainsKey(nameof(IContractResolver)))
            swaggerDoc.Components.Schemas.Remove(nameof(IContractResolver));
    }

    private static void FixOperationSchemas(OpenApiDocument swaggerDoc)
    {
        IEnumerable<IOpenApiAny> OperationPathFilter(string pathName, OpenApiSchema pathSchema, int arrayDepth = 0)
        {
            yield return new OpenApiString(pathName);

            // pathSchema.Type == SchemaTypes.Object
            if (pathSchema.Reference is not null)
            {
                var properties = pathSchema.Properties.Count == 0
                    ? swaggerDoc.Components.Schemas[pathSchema.Reference.Id].Properties
                    : pathSchema.Properties;

                foreach (var pathSchemaProperty in properties)
                {
                    string nextPathName = $"{pathName}/{pathSchemaProperty.Key}";
                    foreach (var operationItem in OperationPathFilter(nextPathName, pathSchemaProperty.Value))
                        yield return operationItem;
                }
            }
            // pathSchema.Type == SchemaTypes.Array
            else if (pathSchema.Items is not null)
            {
                string nextPathName = $"{pathName}/{{{arrayDepth}}}";
                foreach (var operationItem in OperationPathFilter(nextPathName, pathSchema.Items, arrayDepth + 1))
                    yield return operationItem;
            }
        }

        swaggerDoc.Components.Schemas.Remove(nameof(OperationType));
        var operationSchemas = swaggerDoc.Components.Schemas
            .Where(item => item.Key.EndsWith(nameof(Operation)));

        foreach ((string? operationName, var operationSchema) in operationSchemas)
        {
            string baseName = operationName.Replace(nameof(Operation), "");
            if (swaggerDoc.Components.Schemas.TryGetValue(baseName, out var schema))
            {
                var basePropertyNames = schema.Properties
                    .SelectMany(p => OperationPathFilter("/" + p.Key, p.Value))
                    .ToArray();

                operationSchema.Properties = BuildOperationSchemaProperties(basePropertyNames);
            }
            else
            {
                operationSchema.Properties = BuildDefaultOperationSchemaProperties();
            }
        }
    }

    private static Dictionary<string, OpenApiSchema> BuildOperationSchemaProperties(IList<IOpenApiAny> basePropertyNames) => new()
    {
        {
            "op", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Enum = OperationNameEnum
            }
        },
        {
            "path", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Enum = basePropertyNames
            }
        },
        {
            "from", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Enum = basePropertyNames
            }
        },
        {
            "value", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Example = new OpenApiString("new value")
            }
        },
    };

    private static Dictionary<string, OpenApiSchema> BuildDefaultOperationSchemaProperties() => new()
    {
        {
            "op", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Enum = OperationNameEnum
            }
        },
        {
            "path", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Example = new OpenApiString("/path/to/property")
            }
        },
        {
            "from", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Example = new OpenApiString("/path/to/property")
            }
        },
        {
            "value", new OpenApiSchema
            {
                Type = SwaggerSchemaTypes.String,
                Example = new OpenApiString("new value")
            }
        },
    };

    private static void FixJsonPatchDocumentSchemas(OpenApiDocument swaggerDoc)
    {
        var jsonPatchDocSchemas = swaggerDoc.Components.Schemas
            .Where(item => item.Key.EndsWith(nameof(JsonPatchDocument))
                || item.Value?.Properties != null
                && item.Value.Properties.Any(p => p.Value?.Reference?.Id == nameof(IContractResolver)));

        foreach ((string? schemaName, var schema) in jsonPatchDocSchemas)
        {
            string baseName = schemaName.Replace(nameof(JsonPatchDocument), "");
            schema.Properties = new Dictionary<string, OpenApiSchema>
            {
                ["operations"] = new()
                {
                    Type = SwaggerSchemaTypes.Array,
                    Description = "Array of operations to perform.",
                    Items = new OpenApiSchema
                    {
                        Reference = new OpenApiReference
                        {
                            Id = baseName + nameof(Operation),
                            Type = ReferenceType.Schema,
                        }
                    }
                }
            };
        }
    }

    private static List<IOpenApiAny> OperationNameEnum => new()
    {
        new OpenApiString("add"),
        new OpenApiString("copy"),
        new OpenApiString("move"),
        new OpenApiString("remove"),
        new OpenApiString("replace"),
        new OpenApiString("test"),
        new OpenApiString("invalid"),
    };
}