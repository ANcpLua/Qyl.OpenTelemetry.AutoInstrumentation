namespace Qyl.Build.Contracts;

/// <summary>The contract coverage report: the model holds, the generated artifacts are current, and the
/// counts are printed for the reader.</summary>
static class ContractCoverageReport
{
    public static void Verify()
    {
        var contract = ContractModel.Load();
        ContractModel.VerifyContractModel(contract);
        ContractArtifacts.VerifyGeneratedFiles(contract);
        var counts = ContractModel.ContractCounts(contract);
        Log.Information(
            "contract-coverage-report-ok total={Total} upstream={Upstream} qyl_native={Native} signals={Signals} environment_controls={Controls} instrumentation_options={Options}",
            counts["total_contract_items"],
            counts["upstream_contract_items"],
            counts["qyl_native_contract_items"],
            counts["signal_specific_instrumentation_promises"],
            counts["global_environment_controls"],
            counts["instrumentation_options"]);
    }
}
