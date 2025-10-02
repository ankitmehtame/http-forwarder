using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using http_forwarder_app.Core;
using Microsoft.AspNetCore.Http;

namespace http_forwarder_app.Formatters;

public class RawRequestBodyFormatter : InputFormatter
{
    public RawRequestBodyFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/plain"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/octet-stream"));
    }

    public override bool CanRead(InputFormatterContext context)
    {
        // Accept any request so model binding picks this up for [FromBody] object/string parameters
        // and so we can attempt JSON deserialization for typed models even when Content-Type is missing.
        return true;
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Reset position so action or other components can read the body again
        request.Body.Position = 0;

        // If the target model type is string or object, return the raw string
        if (context.ModelType == typeof(string) || context.ModelType == typeof(object))
        {
            return await InputFormatterResult.SuccessAsync(content);
        }

        // Try to deserialize JSON into the target model type even if Content-Type was not set.
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var model = JsonUtils.Deserialize(content, context.ModelType);
                if (model != null)
                {
                    return await InputFormatterResult.SuccessAsync(model);
                }
            }
        }
        catch (Exception)
        {
            // fall-through to NoValue so other formatters can try or framework can return a 400 as appropriate
        }

        return await InputFormatterResult.NoValueAsync();
    }
}
