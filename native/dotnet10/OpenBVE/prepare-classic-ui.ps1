param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '../../../source/OpenBVE/UserInterface'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$names=@('Bitmap','Font','FontFamily','FontStyle','GraphicsUnit','StringFormat','StringAlignment','Brush','SolidBrush','Brushes','Pen','Pens','Graphics','Image')
$aliases=($names | ForEach-Object {"using $_ = System.Drawing.$_;"}) -join "`n"
$aliases+="`nusing Illustrations = OpenBve.Native.WinFormsIllustrations;`n"
foreach($file in Get-ChildItem $source -Filter 'form*.cs') {
    $text=[IO.File]::ReadAllText($file.FullName)
    $text=$text.Replace('ImageExtensions.FromFile(', 'OpenBve.Native.WinFormsImages.FromFile(')
    $text=$text.Replace('Translations.ListLanguages(', 'OpenBve.Native.ClassicUiSupport.ListLanguages(').Replace('Translations.SelectedLanguage(', 'OpenBve.Native.ClassicUiSupport.SelectedLanguage(')
    $text=$text.Replace('Cursors.ListCursors(', 'OpenBve.Native.ClassicUiSupport.ListCursors(').Replace('Cursors.SelectedCursor(', 'OpenBve.Native.ClassicUiSupport.SelectedCursor(')
    $text=$text.Replace('InputDevicePlugin.CallPluginConfig(', 'OpenBve.Native.ClassicUiSupport.CallPluginConfig(').Replace('InputDevicePlugin.AvailablePlugins[p].Config(this)', 'OpenBve.Native.ClassicUiSupport.CallPluginConfig(this, p)')
    $text=$text.Replace('pictureBoxPackageImage.Image = currentPackage.PackageImage;', 'pictureBoxPackageImage.Image = OpenBve.Native.WinFormsImages.Convert(currentPackage.PackageImage);')
    $text=$text.Replace('Directory.GetAccessControl(directory)', 'new DirectoryInfo(directory).GetAccessControl()')
    $text=[regex]::Replace($text,'namespace\s+[\w.]+\s*\{',('$0'+"`n"+$aliases),1)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $file.Name),$text,[Text.UTF8Encoding]::new($false))
}
