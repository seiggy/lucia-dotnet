<#
.SYNOPSIS
    Records a Squad review-gate approval for a commit so the pre-push hook will
    allow it to be pushed. Run ONLY by Vasquez after a clean review.

.DESCRIPTION
    Writes an approval marker into <git-common-dir>/squad-approvals/<sha>. The
    pre-push hook checks for exactly this file. Because the marker is keyed by
    commit SHA, any new commit invalidates the approval and requires a fresh
    review.

    Run this from inside the worktree of the branch being approved. With no
    -Sha, it approves the current HEAD.

.PARAMETER Sha
    The full commit SHA to approve. Defaults to HEAD of the current worktree.

.PARAMETER Branch
    The branch name (for the audit record). Defaults to the current branch.

.PARAMETER Notes
    Optional free-text review notes stored in the marker for auditability.

.EXAMPLE
    pwsh -File .squad/gate/Approve-Branch.ps1

.EXAMPLE
    pwsh -File .squad/gate/Approve-Branch.ps1 -Notes "Clean: build 0 warn, 298 tests pass"
#>
[CmdletBinding()]
param(
    [string]$Sha,
    [string]$Branch,
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'

if (-not $Sha) {
    $Sha = (git rev-parse HEAD).Trim()
}
else {
    # Normalize/verify the SHA exists and expand to full form.
    $Sha = (git rev-parse --verify "$Sha^{commit}").Trim()
}

if (-not $Branch) {
    $Branch = (git rev-parse --abbrev-ref HEAD).Trim()
}

if ($Branch -notlike 'squad/*') {
    Write-Warning "Branch '$Branch' is not a squad/* branch; the pre-push gate only enforces squad/* branches. Recording approval anyway."
}

$commonDir = (git rev-parse --git-common-dir).Trim()
if (-not [System.IO.Path]::IsPathRooted($commonDir)) {
    # --git-common-dir is relative to the CURRENT directory, not the repo root,
    # so resolve it against the invocation directory. Git always runs the
    # pre-push hook from the worktree root, so resolving here the same way keeps
    # the marker path identical to the one the hook writes/reads.
    $commonDir = (Resolve-Path -LiteralPath $commonDir).Path
}
$approvalDir = Join-Path $commonDir 'squad-approvals'
New-Item -ItemType Directory -Force -Path $approvalDir | Out-Null

$markerPath = Join-Path $approvalDir $Sha
$record = [ordered]@{
    sha        = $Sha
    branch     = $Branch
    approvedBy = 'Vasquez'
    model      = 'gpt-5.6-sol'
    approvedAt = (Get-Date).ToUniversalTime().ToString('o')
    notes      = $Notes
}
$record | ConvertTo-Json -Depth 4 | Set-Content -Path $markerPath -Encoding utf8

Write-Output "APPROVED $Branch @ $Sha"
Write-Output "  marker: $markerPath"
