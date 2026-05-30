param(
    [Parameter(Mandatory = $true)]
    [string]$MessagePath,
    [string]$RepoRoot = $env:YUCP_REPO_ROOT
)

$ErrorActionPreference = "Stop"
$HookName = "commit-msg"

function Write-Step {
    param([string]$Message)
    Write-Host "[$HookName] $Message"
}

function Stop-Commit {
    param([string]$Message)
    Write-Host ""
    Write-Host $Message -ForegroundColor Red
    exit 1
}

function Get-CandidateRoots {
    param([string]$FolderKind)

    $roots = New-Object System.Collections.Generic.List[string]
    if ($FolderKind -eq "LocalAppData") {
        $roots.Add([Environment]::GetFolderPath("LocalApplicationData"))
        $roots.Add($env:LOCALAPPDATA)
        foreach ($root in @($env:USERPROFILE, [Environment]::GetFolderPath("UserProfile"))) {
            if (-not [string]::IsNullOrWhiteSpace($root)) {
                $roots.Add((Join-Path $root "AppData\Local"))
            }
        }
    }
    elseif ($FolderKind -eq "UserProfile") {
        $roots.Add([Environment]::GetFolderPath("UserProfile"))
        $roots.Add($env:USERPROFILE)
        $roots.Add($env:HOME)
    }
    elseif ($FolderKind -eq "AppData") {
        $roots.Add([Environment]::GetFolderPath("ApplicationData"))
        $roots.Add($env:APPDATA)
        if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            $roots.Add((Join-Path $env:USERPROFILE "AppData\Roaming"))
        }
    }

    return @($roots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.TrimEnd("\", "/") } |
        Select-Object -Unique)
}

function Get-FirstCommand {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) {
            return @($command)[0].Source
        }

        $knownPaths = New-Object System.Collections.Generic.List[string]
        switch -Regex ($name) {
            '^git(?:\.exe)?$' {
                $knownPaths.Add("$env:ProgramFiles\Git\cmd\git.exe")
                $knownPaths.Add("$env:ProgramFiles\Git\bin\git.exe")
                $knownPaths.Add("${env:ProgramFiles(x86)}\Git\cmd\git.exe")
                $knownPaths.Add("${env:ProgramFiles(x86)}\Git\bin\git.exe")
                foreach ($root in (Get-CandidateRoots "LocalAppData")) {
                    Get-ChildItem -Path "$root\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" -ErrorAction SilentlyContinue |
                        Sort-Object FullName -Descending |
                        ForEach-Object { $knownPaths.Add($_.FullName) }
                }
            }
            '^cr(?:\.exe)?$' {
                $knownPaths.Add($env:CODERABBIT_CLI)
                $knownPaths.Add($env:CODERABBIT_CLI_PATH)
                foreach ($root in (Get-CandidateRoots "LocalAppData")) {
                    $knownPaths.Add((Join-Path $root "Programs\CodeRabbit\bin\cr.exe"))
                }
            }
            '^coderabbit(?:\.exe)?$' {
                $knownPaths.Add($env:CODERABBIT_CLI)
                $knownPaths.Add($env:CODERABBIT_CLI_PATH)
                foreach ($root in (Get-CandidateRoots "LocalAppData")) {
                    $knownPaths.Add((Join-Path $root "Programs\CodeRabbit\bin\coderabbit.exe"))
                }
            }
            '^agy(?:\.exe)?$' {
                $knownPaths.Add($env:AGY_CLI)
                $knownPaths.Add($env:AGY_CLI_PATH)
                foreach ($root in (Get-CandidateRoots "LocalAppData")) {
                    $knownPaths.Add((Join-Path $root "agy\bin\agy.exe"))
                    $knownPaths.Add((Join-Path $root "Programs\Antigravity\bin\agy.exe"))
                }
            }
            '^gemini(?:\.exe|\.cmd)?$' {
                $knownPaths.Add($env:GEMINI_CLI)
                $knownPaths.Add($env:GEMINI_CLI_PATH)
                foreach ($root in (Get-CandidateRoots "UserProfile")) {
                    $knownPaths.Add((Join-Path $root ".bun\bin\gemini.exe"))
                }
                foreach ($root in (Get-CandidateRoots "AppData")) {
                    $knownPaths.Add((Join-Path $root "npm\gemini.cmd"))
                    $knownPaths.Add((Join-Path $root "npm\gemini.exe"))
                }
            }
            '^opencode(?:\.exe|\.cmd)?$' {
                $knownPaths.Add($env:OPENCODE_CLI)
                $knownPaths.Add($env:OPENCODE_CLI_PATH)
                foreach ($root in (Get-CandidateRoots "UserProfile")) {
                    $knownPaths.Add((Join-Path $root ".bun\bin\opencode.exe"))
                }
                foreach ($root in (Get-CandidateRoots "AppData")) {
                    $knownPaths.Add((Join-Path $root "npm\opencode.cmd"))
                    $knownPaths.Add((Join-Path $root "npm\opencode.exe"))
                }
            }
        }

        foreach ($knownPath in ($knownPaths | Select-Object -Unique)) {
            if (-not [string]::IsNullOrWhiteSpace($knownPath) -and (Test-Path -LiteralPath $knownPath)) {
                return $knownPath
            }
        }
    }

    return $null
}

function Invoke-ProcessCapture {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [string]$StandardInput = $null
    )

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo.FileName = $FilePath
    $process.StartInfo.WorkingDirectory = (Get-Location).Path
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardInput = $null -ne $StandardInput
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true

    $process.StartInfo.Arguments = (@($Arguments | ForEach-Object {
        if ($_ -eq "") { '""' }
        elseif ($_ -match '[\s"]') { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
        else { $_ }
    }) -join " ")

    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        if ($PSVersionTable.PSVersion.Major -ge 7) {
            try { $process.Kill($true) } catch { $process.Kill() }
        }
        else {
            Stop-ProcessTree $process.Id
            $process.Kill()
        }
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = 124; StdOut = $stdoutTask.GetAwaiter().GetResult(); StdErr = $stderrTask.GetAwaiter().GetResult(); TimedOut = $true }
    }

    return [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdoutTask.GetAwaiter().GetResult(); StdErr = $stderrTask.GetAwaiter().GetResult(); TimedOut = $false }
}

function Stop-ProcessTree {
    param([int]$ProcessId)

    $descendants = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($ProcessId)

    while ($queue.Count -gt 0) {
        $parentId = $queue.Dequeue()
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$parentId" -ErrorAction SilentlyContinue |
            ForEach-Object {
                $childId = [int]$_.ProcessId
                $descendants.Add($childId)
                $queue.Enqueue($childId)
            }
    }

    foreach ($childId in ($descendants | Select-Object -Unique | Sort-Object -Descending)) {
        Stop-Process -Id $childId -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-GitText {
    param([string[]]$Arguments)

    $result = Invoke-ProcessCapture $script:Git $Arguments 60
    if ($result.TimedOut -or $result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($result.StdErr)) {
            Write-Host $result.StdErr.Trim()
        }
        Stop-Commit "Git command failed: git $($Arguments -join ' ')"
    }
    return $result.StdOut
}

function ConvertTo-RedactedText {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return "" }
    $redacted = $Text
    $redacted = $redacted -replace '(?i)(api[_-]?key|access[_-]?token|auth[_-]?token|secret|password|client[_-]?secret)(\s*[:=]\s*)(["'']?)[^"''\s,;]+', '$1$2$3[REDACTED]'
    $redacted = $redacted -replace '(?i)(bearer\s+)[a-z0-9._~+/=-]+', '$1[REDACTED]'
    $redacted = $redacted -replace 'ghp_[A-Za-z0-9_]+', 'ghp_[REDACTED]'
    $redacted = $redacted -replace 'github_pat_[A-Za-z0-9_]+', 'github_pat_[REDACTED]'
    $redacted = $redacted -replace 'sk-[A-Za-z0-9_-]+', 'sk-[REDACTED]'
    $redacted = $redacted -replace 'C:\\Users\\[^\\\s]+', 'C:\Users\[REDACTED]'
    return $redacted
}

function Get-CommitContext {
    $stat = Invoke-GitText @("diff", "--cached", "--stat")
    $nameStatus = Invoke-GitText @("diff", "--cached", "--name-status")
    $diffSection = "Full staged diff was not sent. Set COMMITMSG_SEND_DIFF=1 to opt in."
    if ($env:COMMITMSG_SEND_DIFF -eq "1" -or $env:PRECOMMIT_SEND_DIFF -eq "1") {
        $diff = ConvertTo-RedactedText (Invoke-GitText @("diff", "--cached", "--unified=20", "--", ".", ":(exclude)Website/CHANGELOG.md"))
        if ($diff.Length -gt 30000) {
            $diff = $diff.Substring(0, 30000) + [Environment]::NewLine + "[diff truncated]"
        }
        $diffSection = $diff
    }

    return @"
STAGED CHANGE STAT:
$stat

STAGED NAME STATUS:
$nameStatus

STAGED DIFF:
$diffSection
"@
}

function Normalize-ModelText {
    param([string]$Text)
    if ($null -eq $Text) { return "" }
    $normalized = ($Text -replace "`e\[[0-9;?]*[ -/]*[@-~]", "").Trim()
    $normalized = (($normalized -split "`r?`n") |
        Where-Object { -not $_.TrimStart().StartsWith(">") }) -join [Environment]::NewLine
    $normalized = $normalized.Trim()
    $normalized = $normalized -replace '^\s*```(?:text|gitcommit|markdown|md)?\s*', ''
    $normalized = $normalized -replace '\s*```\s*$', ''
    return $normalized.Trim()
}

function Test-ConventionalCommitMessage {
    param([string]$Message)
    $firstLine = ($Message -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    return $firstLine -match '^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\([a-z0-9._-]+\))?(!)?: .{1,72}$'
}

function New-CommitPrompt {
    param([string]$Context)
    return @"
Generate a Git commit message for the staged changes.

Return only the commit message. Do not use code fences.
Follow Conventional Commits exactly:
- First line format: type(scope): short imperative summary
- Allowed types: build, chore, ci, docs, feat, fix, perf, refactor, revert, style, test
- Keep the first line 72 characters or less.
- Add a blank line, then a concise body with 1-4 bullets or sentences.
- Do not mention "generate".
- Do not invent issue numbers or links.

$Context
"@
}

function Get-FirstJsonObject {
    param([string]$Text)
    $start = $Text.IndexOf("{")
    if ($start -lt 0) { return $null }
    $depth = 0; $inString = $false; $escaped = $false
    for ($i = $start; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($inString) {
            if ($escaped) { $escaped = $false }
            elseif ($ch -eq "\") { $escaped = $true }
            elseif ($ch -eq '"') { $inString = $false }
            continue
        }
        if ($ch -eq '"') { $inString = $true }
        elseif ($ch -eq "{") { $depth++ }
        elseif ($ch -eq "}") {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($start, $i - $start + 1) }
        }
    }
    return $null
}

function Invoke-AgyText {
    param([string]$Prompt)
    $agy = Get-FirstCommand @("agy")
    if (-not $agy) { return $null }
    Write-Step "Generating commit message with agy..."
    $result = Invoke-ProcessCapture $agy @("--print", "--print-timeout", "45s") 60 $Prompt
    if ($result.TimedOut -or $result.ExitCode -ne 0) { return $null }
    return Normalize-ModelText $result.StdOut
}

function Invoke-GeminiText {
    param([string]$Prompt)
    $gemini = Get-FirstCommand @("gemini", "gemini.cmd", "gemini.exe")
    if (-not $gemini) { return $null }
    Write-Step "Generating commit message with Gemini CLI..."
    $result = Invoke-ProcessCapture $gemini @("-p", "", "--output-format", "json", "--skip-trust") 180 $Prompt
    if ($result.TimedOut -or $result.ExitCode -ne 0) { return $null }
    $jsonText = Get-FirstJsonObject ($result.StdOut + [Environment]::NewLine + $result.StdErr)
    if (-not $jsonText) { return $null }
    try {
        $json = $jsonText | ConvertFrom-Json
    }
    catch {
        return $null
    }
    return Normalize-ModelText $json.response
}

function Invoke-OpenCodeText {
    param([string]$Prompt)
    $opencode = Get-FirstCommand @("opencode", "opencode.cmd", "opencode.exe")
    if (-not $opencode) { return $null }
    $model = if ([string]::IsNullOrWhiteSpace($env:OPENCODE_MODEL)) { "opencode/deepseek-v4-flash-free" } else { $env:OPENCODE_MODEL }
    Write-Step "Running opencode ($model)..."
    $result = Invoke-ProcessCapture $opencode @("run", "-m", $model, $Prompt) 180
    if ($result.TimedOut -or $result.ExitCode -ne 0) { return $null }
    return Normalize-ModelText $result.StdOut
}

function Invoke-CommitMessageGeneration {
    param([string]$Prompt)
    $providers = @("agy", "gemini", "opencode")
    if ($env:COMMITMSG_AI_PROVIDER -eq "opencode" -or $env:PRECOMMIT_AI_PROVIDER -eq "opencode") {
        $providers = @("opencode", "agy", "gemini")
    }

    foreach ($provider in $providers) {
        $message = switch ($provider) {
            "agy" { Invoke-AgyText $Prompt }
            "gemini" { Invoke-GeminiText $Prompt }
            "opencode" { Invoke-OpenCodeText $Prompt }
        }
        if (Test-ConventionalCommitMessage $message) {
            return $message
        }
    }
    return $null
}

function Invoke-CodeRabbitReview {
    $codeRabbit = Get-FirstCommand @("cr", "coderabbit")
    if (-not $codeRabbit) {
        $opencode = Get-FirstCommand @("opencode", "opencode.cmd", "opencode.exe")
        if ($opencode) {
            Write-Step "CodeRabbit unavailable; using opencode fallback review."
            $model = if ([string]::IsNullOrWhiteSpace($env:OPENCODE_MODEL)) { "opencode/deepseek-v4-flash-free" } else { $env:OPENCODE_MODEL }
            $prompt = "Review the staged changes for blocking bugs. Return exactly PASS if there are no blocking issues. Otherwise return concise findings.`n`n$(Get-CommitContext)"
            $result = Invoke-ProcessCapture $opencode @("run", "-m", $model, $prompt) 240
            if ($result.TimedOut -or $result.ExitCode -ne 0) {
                Stop-Commit "opencode fallback review failed."
            }
            $review = Normalize-ModelText $result.StdOut
            if ($review -notmatch '^\s*PASS\.?\s*$') {
                Write-Host $review
                Stop-Commit "opencode fallback review found issues."
            }
            return
        }
        Stop-Commit "CodeRabbit CLI was not found and opencode fallback is unavailable."
    }

    Write-Step "Running CodeRabbit on staged text files..."
    $reviewFiles = @((Invoke-GitText @("diff", "--cached", "--name-only", "--diff-filter=ACMR")) -split "`r?`n" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and [System.IO.Path]::GetExtension($_).ToLowerInvariant() -notin @(".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".fbx", ".unitypackage", ".zip") })
    if ($reviewFiles.Count -eq 0) { return }

    $result = Invoke-ProcessCapture $codeRabbit (@("review", "--agent", "--type", "uncommitted", "--no-color", "--files") + $reviewFiles) 900
    if ($result.TimedOut -or $result.ExitCode -ne 0) {
        $message = if ([string]::IsNullOrWhiteSpace($result.StdErr)) { "CodeRabbit review failed." } else { $result.StdErr.Trim() }
        Write-Host $message
        $fallback = Invoke-OpenCodeText "Review the staged changes for blocking bugs. Return exactly PASS if no blocking issues. Otherwise return concise findings.`n`n$(Get-CommitContext)"
        if ($fallback -match '^\s*PASS\.?\s*$') { return }
        if (-not [string]::IsNullOrWhiteSpace($fallback)) { Write-Host $fallback }
        Stop-Commit "CodeRabbit failed and fallback review did not pass."
    }

    $findings = @()
    foreach ($line in ($result.StdOut -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $event = $line | ConvertFrom-Json
            if ($event.type -eq "finding") { $findings += $event }
        }
        catch {
            continue
        }
    }
    if ($findings.Count -gt 0) {
        Write-Host "CodeRabbit found $($findings.Count) issue(s):"
        foreach ($finding in ($findings | Select-Object -First 25)) {
            $details = if ($finding.codegenInstructions) { $finding.codegenInstructions } elseif ($finding.message) { $finding.message } else { "" }
            Write-Host "- $($finding.fileName): $details"
        }
        Stop-Commit "Fix or intentionally bypass CodeRabbit findings before committing."
    }
}

$script:Git = Get-FirstCommand @("git", "git.exe")
if (-not $script:Git) { Stop-Commit "Git was not found." }

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $rootResult = Invoke-ProcessCapture $script:Git @("rev-parse", "--show-toplevel") 30
    if ($rootResult.TimedOut -or $rootResult.ExitCode -ne 0) { Stop-Commit "Could not determine repository root." }
    $RepoRoot = ($rootResult.StdOut -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
}

Set-Location -LiteralPath $RepoRoot

if (-not (Test-Path -LiteralPath $MessagePath)) {
    Stop-Commit "Commit message file was not found: $MessagePath"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$message = [System.IO.File]::ReadAllText($MessagePath, $utf8NoBom)
$title = (($message -split "`r?`n") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.TrimStart().StartsWith("#") } | Select-Object -First 1)
$title = if ($title) { $title.Trim() } else { "" }
$shouldGenerate = $title -match '(?i)^generate(\s+no\s+review)?$'
$skipReview = $title -match '(?i)^generate\s+no\s+review$'

if ($shouldGenerate) {
    $prompt = New-CommitPrompt (Get-CommitContext)
    $generated = Invoke-CommitMessageGeneration $prompt
    if (-not (Test-ConventionalCommitMessage $generated)) {
        Stop-Commit "Could not generate a valid Conventional Commit message."
    }
    [System.IO.File]::WriteAllText($MessagePath, ($generated.Trim() + [Environment]::NewLine), $utf8NoBom)
    Write-Step "Generated Conventional Commit message."
}
elseif (-not (Test-ConventionalCommitMessage $message)) {
    Stop-Commit "Commit message must follow Conventional Commits, or use 'Generate' / 'Generate no review'."
}

if ($skipReview) {
    Write-Step "Skipping CodeRabbit review because title was 'Generate no review'."
}
else {
    Invoke-CodeRabbitReview
}

exit 0
