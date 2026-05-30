param(
    [switch]$SkipCodeRabbit,
    [switch]$SkipAgyChangelog,
    [switch]$CheckToolsOnly,
    [string]$RepoRoot = $env:YUCP_REPO_ROOT
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[pre-commit] $Message"
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
        if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            $roots.Add((Join-Path $env:USERPROFILE "AppData\Local"))
        }
        $userProfile = [Environment]::GetFolderPath("UserProfile")
        if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
            $roots.Add((Join-Path $userProfile "AppData\Local"))
        }
    }
    elseif ($FolderKind -eq "UserProfile") {
        $roots.Add([Environment]::GetFolderPath("UserProfile"))
        $roots.Add($env:USERPROFILE)
        if (-not [string]::IsNullOrWhiteSpace($env:HOME)) {
            $roots.Add($env:HOME)
        }
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
            default {
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

function Resolve-GitCommand {
    $git = Get-FirstCommand @("git", "git.exe")
    if ($git) {
        return $git
    }

    $candidates = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "$env:ProgramFiles\Git\bin\git.exe",
        "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
        "${env:ProgramFiles(x86)}\Git\bin\git.exe"
    )

    $desktopGit = @(
        Get-ChildItem -Path "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            ForEach-Object { $_.FullName }
    )

    $desktopGitBin = @(
        Get-ChildItem -Path "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\mingw64\bin\git.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            ForEach-Object { $_.FullName }
    )

    foreach ($candidate in ($candidates + $desktopGit + $desktopGitBin)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Read-TextFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return [System.IO.File]::ReadAllText($Path)
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function ConvertTo-RedactedText {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ""
    }

    $redacted = $Text
    $redacted = $redacted -replace '(?i)(api[_-]?key|access[_-]?token|auth[_-]?token|secret|password|client[_-]?secret)(\s*[:=]\s*)(["'']?)[^"''\s,;]+', '$1$2$3[REDACTED]'
    $redacted = $redacted -replace '(?i)(bearer\s+)[a-z0-9._~+/=-]+', '$1[REDACTED]'
    $redacted = $redacted -replace 'ghp_[A-Za-z0-9_]+', 'ghp_[REDACTED]'
    $redacted = $redacted -replace 'github_pat_[A-Za-z0-9_]+', 'github_pat_[REDACTED]'
    $redacted = $redacted -replace 'sk-[A-Za-z0-9_-]+', 'sk-[REDACTED]'
    $redacted = $redacted -replace 'C:\\Users\\[^\\\s]+', 'C:\Users\[REDACTED]'
    $redacted = $redacted -replace '/Users/[^/\s]+', '/Users/[REDACTED]'

    return $redacted
}

function New-ChangelogPrompt {
    param(
        [string]$Today,
        [string]$ExistingChangelog,
        [string]$ChangeSummary
    )

    return @"
Generate the public Markdown changelog for the VRChat Creator Companion package listing in this repository.

Return only the complete Markdown contents for Website/CHANGELOG.md. Do not wrap the answer in code fences.
Use only the provided context below. Do not ask to inspect files or run commands.

Requirements:
- Start with '# Changelog'.
- Add or update the first release entry for $Today.
- Keep useful prior entries from the existing changelog below the new entry.
- Use concise user-facing bullets grouped by package when possible.
- Mention both package names only when the staged changes affect both.
- Do not invent version numbers, issue numbers, or links that are not present in the provided context.
- Ignore changes to Website/CHANGELOG.md itself when deciding what changed.

Known packages:
- com.yucp.devtools
- com.yucp.motion

Existing Website/CHANGELOG.md:
$ExistingChangelog

$ChangeSummary
"@
}

function Normalize-ChangelogText {
    param([string]$Content)

    if ($null -eq $Content) {
        return ""
    }

    $normalized = $Content.Trim()
    $normalized = $normalized -replace '^\s*```(?:markdown|md)?\s*', ''
    $normalized = $normalized -replace '\s*```\s*$', ''
    return $normalized.Trim()
}

function Test-ValidChangelog {
    param([string]$Content)

    $normalized = Normalize-ChangelogText $Content
    return (-not [string]::IsNullOrWhiteSpace($normalized)) -and $normalized.StartsWith("# Changelog")
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

    $quotedArguments = @($Arguments | ForEach-Object {
        if ($_ -eq "") {
            '""'
        }
        elseif ($_ -match '[\s"]') {
            '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
        }
        else {
            $_
        }
    })
    $process.StartInfo.Arguments = $quotedArguments -join " "

    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        if ($PSVersionTable.PSVersion.Major -ge 7) {
            try {
                $process.Kill($true)
            }
            catch {
                $process.Kill()
            }
        }
        else {
            Stop-ProcessTree $process.Id
            $process.Kill()
        }
        $process.WaitForExit()

        return [pscustomobject]@{
            ExitCode = 124
            StdOut = $stdoutTask.GetAwaiter().GetResult()
            StdErr = $stderrTask.GetAwaiter().GetResult()
            TimedOut = $true
        }
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StdOut = $stdoutTask.GetAwaiter().GetResult()
        StdErr = $stderrTask.GetAwaiter().GetResult()
        TimedOut = $false
    }
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
    if ($result.TimedOut) {
        Stop-Commit "Git command timed out: git $($Arguments -join ' ')"
    }

    if ($result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($result.StdErr)) {
            Write-Host $result.StdErr.Trim()
        }
        Stop-Commit "Git command failed: git $($Arguments -join ' ')"
    }

    return $result.StdOut
}

function Get-StagedChangeSummary {
    $stat = Invoke-GitText @("diff", "--cached", "--stat")
    $nameStatus = Invoke-GitText @("diff", "--cached", "--name-status")
    $diffSection = "Full staged diff was not sent. Set PRECOMMIT_SEND_DIFF=1 to opt in."

    if ($env:PRECOMMIT_SEND_DIFF -eq "1") {
        $diff = ConvertTo-RedactedText (Invoke-GitText @("diff", "--cached", "--unified=20", "--", ".", ":(exclude)Website/CHANGELOG.md"))
        $maxChars = 30000
        if ($diff.Length -gt $maxChars) {
            $diff = $diff.Substring(0, $maxChars) + [Environment]::NewLine + "[diff truncated for changelog generation]"
            Write-Step "PRECOMMIT_SEND_DIFF is enabled; sending a redacted, truncated diff to changelog generation."
        }
        else {
            Write-Step "PRECOMMIT_SEND_DIFF is enabled; sending a redacted diff to changelog generation."
        }
        $diffSection = $diff
    }
    else {
        Write-Step "Not sending full diff to changelog generation. Set PRECOMMIT_SEND_DIFF=1 to opt in."
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

function Get-CodeRabbitReviewFiles {
    $numstat = Invoke-GitText @("diff", "--cached", "--numstat", "--diff-filter=ACMR")
    $reviewFiles = New-Object System.Collections.Generic.List[string]

    foreach ($line in ($numstat -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line -split "`t"
        if ($parts.Count -lt 3) {
            continue
        }

        $added = $parts[0]
        $deleted = $parts[1]
        $path = $parts[$parts.Count - 1]

        if ($added -eq "-" -or $deleted -eq "-") {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
        if ($extension -in @(".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".fbx", ".unitypackage", ".zip")) {
            continue
        }

        $reviewFiles.Add($path)
    }

    return @($reviewFiles | Select-Object -Unique)
}

function Get-FirstJsonObject {
    param([string]$Text)

    $start = $Text.IndexOf("{")
    if ($start -lt 0) {
        return $null
    }

    $depth = 0
    $inString = $false
    $escaped = $false

    for ($i = $start; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]

        if ($inString) {
            if ($escaped) {
                $escaped = $false
            }
            elseif ($ch -eq "\") {
                $escaped = $true
            }
            elseif ($ch -eq '"') {
                $inString = $false
            }
            continue
        }

        if ($ch -eq '"') {
            $inString = $true
        }
        elseif ($ch -eq "{") {
            $depth++
        }
        elseif ($ch -eq "}") {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($start, $i - $start + 1)
            }
        }
    }

    return $null
}

function Invoke-AgyChangelog {
    param([string]$Prompt)

    $agy = Get-FirstCommand @("agy")
    if (-not $agy) {
        return $null
    }

    Write-Step "Trying changelog generation with agy..."
    $result = Invoke-ProcessCapture $agy @("--print", "--print-timeout", "45s") 60 $Prompt

    if ($result.TimedOut) {
        Write-Step "agy timed out; falling back to Gemini CLI."
        return $null
    }

    if ($result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($result.StdErr)) {
            Write-Host $result.StdErr.Trim()
        }
        return $null
    }

    $content = Normalize-ChangelogText $result.StdOut
    if (Test-ValidChangelog $content) {
        return $content
    }

    Write-Step "agy did not return a valid changelog; falling back to Gemini CLI."
    return $null
}

function Invoke-GeminiChangelog {
    param([string]$Prompt)

    $gemini = Get-FirstCommand @("gemini")
    if (-not $gemini) {
        return $null
    }

    Write-Step "Generating changelog with Gemini CLI..."
    $result = Invoke-ProcessCapture $gemini @("-p", "", "--output-format", "json", "--skip-trust") 180 $Prompt

    if ($result.TimedOut) {
        Write-Step "Gemini CLI timed out."
        return $null
    }

    if ($result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($result.StdErr)) {
            Write-Host $result.StdErr.Trim()
        }
        return $null
    }

    $raw = $result.StdOut + [Environment]::NewLine + $result.StdErr
    $jsonText = Get-FirstJsonObject $raw
    if (-not $jsonText) {
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            Write-Host "Gemini did not return parseable JSON:"
            Write-Host ($raw.Substring(0, [Math]::Min(1000, $raw.Length)))
        }
        return $null
    }

        try {
            $json = $jsonText | ConvertFrom-Json
        }
        catch {
            Write-Host "Gemini returned malformed JSON:"
            Write-Host (($raw + [Environment]::NewLine + $jsonText).Substring(0, [Math]::Min(1000, ($raw + [Environment]::NewLine + $jsonText).Length)))
            return $null
        }
        $content = Normalize-ChangelogText $json.response
    if (Test-ValidChangelog $content) {
        return $content
    }

    if (-not [string]::IsNullOrWhiteSpace($content)) {
        Write-Host "Gemini returned an invalid changelog response:"
        Write-Host ($content.Substring(0, [Math]::Min(1000, $content.Length)))
    }

    return $null
}

$script:Git = Resolve-GitCommand
if (-not $script:Git) {
    Stop-Commit "Git was not found. Install Git for Windows or run the commit from an environment where git.exe is available."
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $repoRootResult = Invoke-ProcessCapture $script:Git @("rev-parse", "--show-toplevel") 30
    if ($repoRootResult.TimedOut -or $repoRootResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($repoRootResult.StdOut)) {
        Stop-Commit "Could not determine the git repository root."
    }

    $RepoRoot = ($repoRootResult.StdOut -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    Stop-Commit "Could not determine the git repository root."
}

$repoRoot = $RepoRoot.Trim()
Set-Location -LiteralPath $repoRoot

$stagedFiles = @(
    (Invoke-GitText @("diff", "--cached", "--name-only", "--diff-filter=ACMR")) -split "`r?`n" |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

if ($stagedFiles.Count -eq 0) {
    Write-Step "No staged files to review."
    exit 0
}

$codeRabbit = Get-FirstCommand @("cr", "coderabbit")
$agy = Get-FirstCommand @("agy")
$gemini = Get-FirstCommand @("gemini", "gemini.cmd", "gemini.exe")

if ($CheckToolsOnly) {
    if (-not $codeRabbit) {
        Stop-Commit "CodeRabbit CLI was not found. Set CODERABBIT_CLI_PATH to cr.exe or install CodeRabbit CLI."
    }

    if (-not $agy -and -not $gemini) {
        Stop-Commit "Neither agy nor Gemini CLI was found. Set AGY_CLI_PATH or GEMINI_CLI_PATH, or install one of them."
    }

    Write-Step "CodeRabbit CLI: $codeRabbit"
    if ($agy) {
        Write-Step "agy CLI: $agy"
    }
    if ($gemini) {
        Write-Step "Gemini CLI: $gemini"
    }
    exit 0
}

Write-Step "CodeRabbit review runs in commit-msg so commit titles like 'Generate no review' can be honored."

if (-not $SkipAgyChangelog) {
    $changelogPath = Join-Path $repoRoot "Website/CHANGELOG.md"
    $originalChangelog = Read-TextFile $changelogPath
    $today = Get-Date -Format "yyyy-MM-dd"
    $changeSummary = Get-StagedChangeSummary
    $prompt = New-ChangelogPrompt -Today $today -ExistingChangelog $originalChangelog -ChangeSummary $changeSummary

    $content = Invoke-AgyChangelog $prompt
    if (-not (Test-ValidChangelog $content)) {
        $content = Invoke-GeminiChangelog $prompt
    }

    if (-not (Test-ValidChangelog $content)) {
        Write-Utf8File $changelogPath $originalChangelog
        Stop-Commit "Neither agy nor Gemini CLI produced a valid Markdown changelog, so the commit was stopped."
    }

    $content = Normalize-ChangelogText $content
    Write-Utf8File $changelogPath ($content + [Environment]::NewLine)
    [void](Invoke-GitText @("add", "--", "Website/CHANGELOG.md"))

    Write-Step "Updated and staged Website/CHANGELOG.md."
}
else {
    Write-Step "Skipping changelog generation because -SkipAgyChangelog was supplied."
}

exit 0
