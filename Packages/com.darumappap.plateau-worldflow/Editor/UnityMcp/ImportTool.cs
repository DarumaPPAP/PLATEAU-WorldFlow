using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DarumaPPAP.PlateauWorldFlow.Entry;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DarumaPPAP.PlateauWorldFlow.UnityMcp
{
    /// <summary>
    /// Unity MCPからImport要求を共有Editorセッションへ渡す。
    /// </summary>
    [McpForUnityTool("worldflow_import", Description = "Execute a WorldFlow v1 Import request. Import execution requires approval in the Unity Editor.")]
    public sealed class ImportTool
    {
        /// <summary>
        /// Import要求のJSON文字列をMCPの引数として公開する。
        /// </summary>
        public sealed class Parameters
        {
            [ToolParameter("WorldFlow v1 Import request as a JSON string.")]
            public string Request { get; set; }
        }

        /// <summary>
        /// Import応答を構造化データとして返す。
        /// </summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            var request = parameters?.GetValue("Request", StringComparison.OrdinalIgnoreCase);
            // 実行条件はすべて共有契約の入力に集約する。
            if (parameters == null || parameters.Count != 1 || request?.Type != JTokenType.String) return new ErrorResponse("WorldFlow requires an Import request JSON string.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                var response = await ImportSession.Current.ExecuteAsync(request.Value<string>(), timeout.Token);
                using var reader = new JsonTextReader(new StringReader(response)) { DateParseHandling = DateParseHandling.None };
                return new SuccessResponse("WorldFlow Import response", JObject.Load(reader));
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is OperationCanceledException)
            {
                return new ErrorResponse("WorldFlow Import request rejected or unavailable.");
            }
        }
    }
}
