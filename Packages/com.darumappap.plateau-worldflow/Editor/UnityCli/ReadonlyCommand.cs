using System;
using System.IO;
using System.Threading.Tasks;
using DarumaPPAP.PlateauWorldFlow.Entry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace DarumaPPAP.PlateauWorldFlow.UnityCli
{
    /// <summary>
    /// Unity公式CLIからWorldFlowの読取契約を呼び出し、判定に対応する終了コードを返す。
    /// </summary>
    internal sealed class ReadonlyCommand
    {
        /// <summary>
        /// 共通のEditorセッションへ要求を渡し、v1応答をPipelineの結果として出力する。
        /// </summary>
        [CliCommand("worldflow", "readonly", "Execute a WorldFlow v1 request; areas.preview_png saves Project-local evidence.", Effect = CommandEffect.Additive)]
        internal static async Task<ExitCode> Execute(CommandContext context, [CliArg("request", "WorldFlow v1 request as a JSON string.", Required = true)] string request, [CliArg("status-json", "Print the contract result even for a non-ready status.")] bool statusJson = false)
        {
            try
            {
                string json = await Session.Current.ExecuteAsync(request, context.CancellationToken);
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
                var response = JObject.Load(reader);
                context.Result.Write(response);
                // Pipeline 1.0は非ゼロ終了時に結果を抑制する。JSONを指定した呼び出し元はstatusを評価する。
                if (statusJson) return ExitCode.Success;

                switch (response.Value<string>("status"))
                {
                    case "READY":
                    case "PARTIAL": return ExitCode.Success;
                    case "UNAVAILABLE": return ExitCode.NotFound;
                    case "FAILED":
                    {
                        string code = response["diagnostics"]?[0]?["code"]?.Value<string>();
                        return code == "INVALID_INPUT" || code == "INVALID_GRID_CODE" || code == "LIMIT_EXCEEDED" ? ExitCode.InvalidUsage : ExitCode.Failed;
                    }
                    default: return ExitCode.Failed;
                }
            }
            catch (FormatException)
            {
                context.Log.Error("Invalid WorldFlow request.");
                return ExitCode.InvalidUsage;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                context.Log.Error("WorldFlow is unavailable in this Editor.");
                return ExitCode.Failed;
            }
        }
    }
}
