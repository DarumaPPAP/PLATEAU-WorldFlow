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
    /// Unity公式CLIからImport要求を共有Editorセッションへ渡す。
    /// </summary>
    internal sealed class ImportCommand
    {
        /// <summary>
        /// 停止理由を含むImport契約の応答をPipelineへ返す。
        /// </summary>
        [CliCommand("worldflow", "import", "Execute a WorldFlow v1 Import request. Import execution requires approval in the Unity Editor.", Effect = CommandEffect.Additive)]
        internal static async Task<ExitCode> Execute(CommandContext context, [CliArg("request", "WorldFlow v1 Import request as a JSON string.", Required = true)] string request)
        {
            try
            {
                string response = await ImportSession.Current.ExecuteAsync(request, context.CancellationToken);
                using var reader = new JsonTextReader(new StringReader(response)) { DateParseHandling = DateParseHandling.None };
                context.Result.Write(JObject.Load(reader));
                return ExitCode.Success;
            }
            catch (FormatException)
            {
                context.Log.Error("Invalid WorldFlow Import request.");
                return ExitCode.InvalidUsage;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is OperationCanceledException)
            {
                context.Log.Error("WorldFlow Import is unavailable in this Editor.");
                return ExitCode.Failed;
            }
        }
    }
}
