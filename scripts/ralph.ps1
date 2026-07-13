# Ralph loop - runs `claude -p` with a fresh context per iteration until the spec's PRD is done.
#
# Flow:
#   1. Resolves Docs/Specs/<SpecIndex>_<name>/ and switches to branch ralph/<index>_<name> (creates it if needed)
#   2. Pre-run:  claude "/create-prd <SpecIndex>"  - builds .ralph/prd.md from the spec's plan.md
#   3. Loop:     claude .ralph/PROMPT.md           - one task per fresh-context iteration
#   4. Post-run: claude "/complete-prd <SpecIndex>" - commits leftovers and opens a PR (only if all tasks passed)
#
# The Unity Editor should be running (Unity MCP is used to verify Unity-side tasks).
#
# Usage (from project root):
#   .\scripts\ralph.ps1 -SpecIndex 45 -MaxIterations 10
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipCreatePrd          # reuse the existing .ralph/prd.md
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipPullRequest        # stop after the loop, no commit/PR phase
#   .\scripts\ralph.ps1 -SpecIndex 45 -DangerouslySkipPermissions
#
# Metrics per phase/iteration are appended to .ralph\metrics_<SpecIndex>.csv (gitignored).

param(
    [Parameter(Mandatory = $true)][int]$SpecIndex,
    [int]$MaxIterations = 10,
    [switch]$SkipCreatePrd,
    [switch]$SkipPullRequest,
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

$promptFile = ".ralph\PROMPT.md"
if (-not (Test-Path $promptFile)) { throw "Missing $promptFile - run from the project root." }
$prdFile = ".ralph\prd.md"
$csvFile = ".ralph\metrics_$SpecIndex.csv"

# --- Resolve spec folder ---
$specDirs = @(Get-ChildItem "Docs\Specs" -Directory | Where-Object { $_.Name -match "^${SpecIndex}_" })
if ($specDirs.Count -eq 0) { throw "No spec folder matches index $SpecIndex under Docs\Specs." }
if ($specDirs.Count -gt 1) { throw "Multiple spec folders match index ${SpecIndex}: $($specDirs.Name -join ', ')" }
$specDir = $specDirs[0]
if (-not (Test-Path (Join-Path $specDir.FullName "plan.md"))) { throw "Spec folder $($specDir.Name) has no plan.md - run /plan first." }
Write-Host "Spec: Docs\Specs\$($specDir.Name)" -ForegroundColor Cyan

# --- Branch: ralph/<index>_<name> ---
$ralphBranch = "ralph/$($specDir.Name)"
$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne $ralphBranch) {
    $dirty = & git status --porcelain
    if ($dirty) { throw "Working tree is not clean - commit or stash before starting a Ralph run." }
    & git rev-parse --verify --quiet "refs/heads/$ralphBranch" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & git checkout $ralphBranch
    } else {
        & git checkout -b $ralphBranch
    }
    if ($LASTEXITCODE -ne 0) { throw "Failed to switch to branch $ralphBranch." }
}
Write-Host "Branch: $ralphBranch" -ForegroundColor Cyan

# --- Metrics ---
if (-not (Test-Path $csvFile)) {
    "phase,iteration,cost_usd,num_turns,input_tokens,output_tokens,cache_read,cache_create,duration_ms,stop_reason" |
        Out-File $csvFile -Encoding utf8
}

# --- Permission allowlists (edits are covered by --permission-mode acceptEdits) ---
# "mcp__UnityMCP" allows every tool of the Unity MCP server (refresh_unity, read_console, manage_* ...).
$loopTools = @(
    "mcp__UnityMCP",
    "Bash(dotnet build:*)", "Bash(dotnet test:*)",
    "Bash(git add:*)", "Bash(git commit:*)", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)",
    "Bash(.venv\Scripts\python.exe:*)",
    "PowerShell(dotnet build *)", "PowerShell(dotnet test *)",
    "PowerShell(git add *)", "PowerShell(git commit *)", "PowerShell(git status *)", "PowerShell(git diff *)", "PowerShell(git log *)",
    "PowerShell(.venv\Scripts\python.exe *)"
) -join ","
$prTools = $loopTools + "," + (@(
    "Bash(git push:*)", "Bash(gh pr create:*)", "Bash(gh pr view:*)",
    "PowerShell(git push *)", "PowerShell(gh pr create *)", "PowerShell(gh pr view *)"
) -join ",")

function Invoke-ClaudeStep {
    param(
        [string]$Phase,
        [string]$Iteration,
        [string]$Prompt,
        [string]$AllowedTools
    )
    $claudeArgs = @("-p", $Prompt, "--output-format", "json")
    if ($DangerouslySkipPermissions) {
        $claudeArgs += "--dangerously-skip-permissions"
    } else {
        $claudeArgs += @("--permission-mode", "acceptEdits", "--allowedTools", $AllowedTools)
    }

    $raw = & claude @claudeArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "claude exited with code $LASTEXITCODE in phase '$Phase'." -ForegroundColor Red
        "$Phase,$Iteration,,,,,,,,claude_error" | Out-File $csvFile -Append -Encoding utf8
        return $null
    }

    $r = ($raw -join "") | ConvertFrom-Json
    $u = $r.usage
    "$Phase,$Iteration,$($r.total_cost_usd),$($r.num_turns),$($u.input_tokens),$($u.output_tokens),$($u.cache_read_input_tokens),$($u.cache_creation_input_tokens),$($r.duration_ms)," |
        Out-File $csvFile -Append -Encoding utf8
    Write-Host ("{0}: cost ${1}  turns {2}  duration {3:N0}s" -f $Phase, $r.total_cost_usd, $r.num_turns, ($r.duration_ms / 1000))
    return $r
}

# --- Phase 1: create PRD from the spec's plan ---
if ($SkipCreatePrd) {
    if (-not (Test-Path $prdFile)) { throw "-SkipCreatePrd was passed but $prdFile does not exist." }
    Write-Host "Skipping /create-prd, reusing existing $prdFile" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "=== Phase: /create-prd $SpecIndex ===" -ForegroundColor Cyan
    $r = Invoke-ClaudeStep -Phase "create-prd" -Iteration "" -Prompt "/create-prd $SpecIndex" -AllowedTools $loopTools
    if ($null -eq $r -or $r.is_error) { throw "/create-prd failed - aborting before the loop." }
    if (-not (Select-String -Path $prdFile -Pattern '"passes":\s*false' -Quiet)) {
        throw "/create-prd produced no open tasks in $prdFile - check its output before looping."
    }
}

# --- Phase 2: the loop ---
$stopReason = "max_iterations"
for ($i = 1; $i -le $MaxIterations; $i++) {
    Write-Host ""
    Write-Host "=== Ralph iteration $i / $MaxIterations ($ralphBranch) ===" -ForegroundColor Cyan

    $prompt = Get-Content $promptFile -Raw
    $r = Invoke-ClaudeStep -Phase "loop" -Iteration $i -Prompt $prompt -AllowedTools $loopTools
    if ($null -eq $r) { $stopReason = "claude_error"; break }

    if ($r.is_error) {
        $stopReason = "result_error"
    } elseif ($r.result -match "<promise>COMPLETE</promise>") {
        $stopReason = "complete_promise"
    } elseif (-not (Select-String -Path $prdFile -Pattern '"passes":\s*false' -Quiet)) {
        $stopReason = "all_tasks_passed"
    }
    if ($stopReason -ne "max_iterations") { break }
}
Write-Host ""
Write-Host "=== Loop finished: $stopReason ===" -ForegroundColor Cyan

# --- Phase 3: commit + pull request ---
$loopSucceeded = ($stopReason -eq "complete_promise") -or ($stopReason -eq "all_tasks_passed")
if ($SkipPullRequest) {
    Write-Host "Skipping /complete-prd (-SkipPullRequest)." -ForegroundColor Yellow
} elseif (-not $loopSucceeded) {
    Write-Host "Loop did not finish all tasks - skipping PR. To create one anyway, run:" -ForegroundColor Yellow
    Write-Host "  claude -p `"/complete-prd $SpecIndex`""
} else {
    Write-Host ""
    Write-Host "=== Phase: /complete-prd $SpecIndex ===" -ForegroundColor Cyan
    $r = Invoke-ClaudeStep -Phase "complete-prd" -Iteration "" -Prompt "/complete-prd $SpecIndex" -AllowedTools $prTools
    if ($null -eq $r -or $r.is_error) {
        Write-Host "/complete-prd failed - commit/PR may need manual attention." -ForegroundColor Red
    } elseif ($r.result) {
        Write-Host $r.result
    }
}

# --- Totals ---
$rows = Import-Csv $csvFile | Where-Object { $_.cost_usd -ne "" }
if ($rows) {
    $totalCost   = ($rows | Measure-Object -Property cost_usd -Sum).Sum
    $totalTurns  = ($rows | Measure-Object -Property num_turns -Sum).Sum
    $totalInput  = ($rows | Measure-Object -Property input_tokens -Sum).Sum
    $totalOutput = ($rows | Measure-Object -Property output_tokens -Sum).Sum
    $totalCacheR = ($rows | Measure-Object -Property cache_read -Sum).Sum
    $totalCacheC = ($rows | Measure-Object -Property cache_create -Sum).Sum
    Write-Host ""
    Write-Host ("TOTAL (all rows in {0}): cost ${1}  turns {2}" -f $csvFile, $totalCost, $totalTurns)
    Write-Host ("tokens: input {0}  output {1}  cache_read {2}  cache_create {3}" -f `
        $totalInput, $totalOutput, $totalCacheR, $totalCacheC)
}
