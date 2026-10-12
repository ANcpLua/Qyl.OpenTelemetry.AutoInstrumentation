namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("Contracts: regenerate the generated contract artifacts instead of checking them")]
    readonly bool WriteContracts;

    Target Contracts => definition => definition
        .Description("Contract invariants, doc pins, the contract coverage report, version sync and the generated contract artifacts")
        .Executes(() =>
        {
            if (WriteContracts)
            {
                var contract = ContractModel.Load();
                ContractModel.VerifyContractModel(contract);
                ContractArtifacts.WriteGeneratedFiles(contract);
                Log.Information("contract-artifacts-generated");
                return;
            }

            ContractInvariants.Verify();
            DocPins.Verify();
            ContractCoverageReport.Verify();
            VersionSync.Verify();
            Log.Information("contracts-ok");
        });
}
