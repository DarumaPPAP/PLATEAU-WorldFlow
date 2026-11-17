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
    /// Unity MCPの単一ツールからWorldFlowの読取契約を受け付ける。
    /// </summary>
    [McpForUnityTool("worldflow_readonly", Description = "Execute a WorldFlow v1 read-only request. Explicit areas.preview_png requests save PNG evidence under Library.")]
    public sealed class ReadonlyTool
    {
        /// <summary>
        /// Unity MCPのツールスキーマにv1 JSON文字列の引数を公開する。
        /// </summary>
        public sealed class Parameters
        {
            [ToolParameter("WorldFlow v1 request as a JSON string.")]
            public string Request { get; set; }
        }

        /// <summary>
        /// 共通のEditorセッションへ要求を渡し、v1応答をMCPのデータとして返す。
        /// </summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            var request = parameters?.GetValue("Request", StringComparison.OrdinalIgnoreCase);
            if (parameters == null || parameters.Count != 1 || request?.Type != JTokenType.String)
            {
                return new ErrorResponse("WorldFlow requires a request JSON string.");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                string response = await Session.Current.ExecuteAsync(request.Value<string>(), timeout.Token);
                using var reader = new JsonTextReader(new StringReader(response)) { DateParseHandling = DateParseHandling.None };
                return new SuccessResponse("WorldFlow response", JObject.Load(reader));
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is OperationCanceledException)
            {
                return new ErrorResponse("WorldFlow request rejected or unavailable.");
            }
        }
    }
}
