param([Parameter(Mandatory=$true)][string]$AppPath, [switch]$VerifyWebAndPdf, [string]$ScreenshotPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$scope = [System.Windows.Automation.TreeScope]::Descendants
function Find-Control($root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $element = $root.FindFirst($scope, $condition)
    if ($null -eq $element) { throw "Control unavailable: $name" }
    return $element
}
function Invoke-Control($root, [string]$name) {
    $element = Find-Control $root $name
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 400
}
function Select-Control($root, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $candidates = $root.FindAll($scope, $condition)
    $pattern = $null
    foreach ($element in $candidates) {
        if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { break }
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
        if ($null -ne $parent -and $parent.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { break }
    }
    if ($null -eq $pattern) {
        throw "Selection unavailable: $name"
    }
    $pattern.Select()
    Start-Sleep -Milliseconds 400
}
function Set-Control($root, [string]$name, [string]$value) {
    $element = Find-Control $root $name
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}
function Assert-Control($root, [string]$name, [string]$label) {
    $null = Find-Control $root $name
    Write-Output "PASS $label"
}
$process = Start-Process -FilePath $AppPath -WindowStyle Normal -PassThru
try {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 400
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before UI became available.' }
        if ($process.MainWindowHandle -ne 0) { break }
    }
    for ($attempt = 0; $attempt -lt 75; $attempt++) {
        $process.Refresh()
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $nav = $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, '主导航'))
        if ($null -ne $nav) { break }
        Start-Sleep -Milliseconds 400
    }
    if ($null -eq $nav) {
        $count = $root.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition).Count
        throw "WPF navigation unavailable: window=$($root.Current.Name), descendants=$count"
    }
    Assert-Control $root '主导航' 'WPF_STARTUP'
    Select-Control $root '知识库'
    Select-Control $root '中文知识库'
    Write-Output 'PASS EXISTING_CHINESE_KNOWLEDGE_BASE_VISIBLE'
    Select-Control $root '文档搜索'
    Set-Control $root '文档搜索词' '墙板'
    Invoke-Control $root '搜索'
    $results = Find-Control $root '文档搜索结果'
    $items = $results.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    if ($items.Count -eq 0) { throw 'Chinese search returned no visible results.' }
    Write-Output "PASS CHINESE_SEARCH_UI results=$($items.Count)"
    Invoke-Control $root '打开来源'
    Assert-Control $root '已打开来源，可核对原文位置。' 'OPEN_SOURCE_UI'
    Set-Control $root '来源页码' '10'
    Invoke-Control $root '跳转'
    Assert-Control $root 'PDF 物理第 10 页 / 共 58 页' 'PDF_TEXT_PAGE_JUMP_UI'
    $editControls = $root.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit))
    $pageTextMatches = $false
    foreach ($edit in $editControls) {
        $value = $null
        if ($edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$value) -and
            $value.Current.Value.Contains('蒸压加气混凝土墙板')) { $pageTextMatches = $true; break }
    }
    if (-not $pageTextMatches) { throw 'Jumped PDF page text does not show expected OCR phrase.' }
    Write-Output 'PASS PDF_PAGE_CONTENT_MATCH_UI'
    if ($VerifyWebAndPdf) {
        Invoke-Control $root '查看原始 PDF'
        $pdfWindow = $null
        $loaded = $null
        for ($attempt = 0; $attempt -lt 75; $attempt++) {
            $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id))
            $ownedWindows = $root.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
            foreach ($window in @($windows) + @($ownedWindows)) {
                if ($window.Current.Name.StartsWith('原始 PDF')) { $pdfWindow = $window; break }
            }
            if ($null -ne $pdfWindow) {
                $loaded = $pdfWindow.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, 'PDF 物理第 10 页 / 共 58 页 · 原始页面'))
                if ($null -ne $loaded) { break }
            }
            Start-Sleep -Milliseconds 400
        }
        if ($null -eq $pdfWindow -or $null -eq $loaded) {
            Write-Output "PDF_WINDOW_FOUND=$($null -ne $pdfWindow)"
            if ($null -ne $pdfWindow) {
                $texts = $pdfWindow.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
                foreach ($text in $texts) { if ($text.Current.Name.StartsWith('PDF ') -or $text.Current.Name.StartsWith('无法') -or $text.Current.Name.StartsWith('正在')) { Write-Output $text.Current.Name } }
            }
            throw 'Original PDF page did not render.'
        }
        Assert-Control $pdfWindow 'PDF 原始页面图像' 'ORIGINAL_PDF_IMAGE_UI'
        if ($ScreenshotPath) {
            Add-Type -AssemblyName System.Drawing
            $pdfWindow.SetFocus()
            Start-Sleep -Milliseconds 300
            $bounds = $pdfWindow.Current.BoundingRectangle
            $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size); $bitmap.Save($ScreenshotPath) }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
        $pdfWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Select-Control $root 'AI问答'
        $toggle = Find-Control $root '联网搜索开关'
        $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Assert-Control $root '公开搜索词' 'WEB_MODE_UI'
        $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    }
    Select-Control $root '设置'
    Assert-Control $root '本地数据目录' 'DATA_DIRECTORY_UI'
    Assert-Control $root 'DeepSeek API Key 输入框' 'KEY_CONFIGURATION_UI'
    if ($VerifyWebAndPdf) { Assert-Control $root 'Tavily 搜索 Key' 'SEARCH_KEY_PASSWORD_UI' }
    Invoke-Control $root '保存 Key'
    Assert-Control $root '请输入 DeepSeek API Key。' 'EMPTY_KEY_ERROR_UI'
    $noKey = $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, '尚未保存 DeepSeek API Key。'))
    if ($null -ne $noKey) {
        Invoke-Control $root '测试连接'
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            $missingKeyMessage = $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, '尚未配置 DeepSeek API Key。'))
            if ($null -ne $missingKeyMessage) { break }
            Start-Sleep -Milliseconds 200
        }
        if ($null -eq $missingKeyMessage) { throw 'Missing-Key connection error was not displayed.' }
        Write-Output 'PASS NO_KEY_CONNECTION_ERROR_UI'
    } else {
        Write-Output 'BLOCKED NO_KEY_CONNECTION_ERROR_UI: Credential already exists.'
    }
    Write-Output 'PASS NAVIGATION_UI'
} finally {
    if (-not $process.HasExited) {
        $closed = $process.CloseMainWindow()
        if (-not $process.WaitForExit(10000)) { throw 'Graceful application shutdown did not complete.' }
        Write-Output "PASS GRACEFUL_CLOSE requested=$closed"
    }
}
