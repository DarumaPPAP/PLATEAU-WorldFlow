using System;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class InstallGate
    {
        private bool _busy;

        internal string Run(InstallPlan plan, Func<InstallContext> current, bool batchMode, Func<bool> confirm, Action persist, Action<string> add)
        {
            // Batch ModeではPlanに紐付ける本人の入力を取得できない。
            if (batchMode) return "BATCH_MODE";

            // 承認ダイアログの表示中にEditorイベントが再入する場合がある。
            if (_busy) return "BUSY";

            _busy = true;
            try
            {
                // 失効済みのPlanは承認対象として提示しない。
                if (!plan.CanExecute(current())) return "PLAN_INVALID";

                var approvalTarget = (plan.Id, plan.Revision, plan.Hash, plan.Project);
                // この実行の承認には、Editor内での本人の応答が必要。
                if (!confirm()) return "APPROVAL_DECLINED";

                var approvedPlan = InstallPlan.Parse(plan.ToJson());
                // 承認を待つ間のProject変更や期限切れを、実行前に再確認する。
                if (approvalTarget != (approvedPlan.Id, approvedPlan.Revision, approvedPlan.Hash, approvedPlan.Project) || !approvedPlan.CanExecute(current())) return "PLAN_INVALID";

                persist();
                add(approvedPlan.Source);
                return "DISPATCHED";
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
