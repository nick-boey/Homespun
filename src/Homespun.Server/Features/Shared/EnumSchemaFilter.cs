using System.Text.Json;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Homespun.Features.Shared;

/// <summary>
/// Schema filter that converts enum types to string schemas with camelCase values.
/// This ensures OpenAPI spec generates string enums matching the JSON serialization.
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
        {
            return;
        }

        // Cast to the concrete type to modify properties
        if (schema is OpenApiSchema concreteSchema)
        {
            // Clear existing enum values (which would be integers)
            concreteSchema.Enum?.Clear();

            // Set the type to string
            concreteSchema.Type = JsonSchemaType.String;

            // Add camelCase string values for each enum member
            foreach (var name in Enum.GetNames(context.Type))
            {
                var camelCaseName = JsonNamingPolicy.CamelCase.ConvertName(name);
                concreteSchema.Enum?.Add(camelCaseName);
            }
        }
    }
}
