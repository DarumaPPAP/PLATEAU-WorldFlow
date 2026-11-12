using System;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class InstallGate
    {
        private bool _busy;

        internal string Run(InstallPlan plan, Func<InstallContext> current, bool batchMode, Func<bool> confirm, Action persist, Action<string> add)
        {
            // A batch session has no local dialog response to bind to the plan.
            if (batchMode) return "BATCH_MODE";

            // Modal dialogs can re-enter the editor event loop while an approval is open.
            if (_busy) return "BUSY";

            _busy = true;
            try
            {
                // Reject stale requests before prompting.
                if (!plan.CanExecute(current())) return "PLAN_INVALID";

                var approvalTarget = (plan.Id, plan.Revision, plan.Hash, plan.Project);
                // Only the local dialog response can authorize this invocation.
                if (!confirm()) return "APPROVAL_DECLINED";

                var approvedPlan = InstallPlan.Parse(plan.ToJson());
                // The dialog may have stayed open while the project or time window changed.
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
