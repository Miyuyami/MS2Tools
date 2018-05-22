# args for MS2Create
$input = "C:\MS2ExtractOutput"
$output = "C:\MS2CreateOutput"
$archiveName = "Image"
$mode = "MS2F"
$syncMode = "0"
$logMode = "3"

$logFileName = "output.log"
$outputLogPath = [System.IO.Path]::Combine($output, $logFileName)

# we need to manually create this so that powershell won't throw an exception if the directory doesn't exist
$outputLogFolder = [System.IO.Path]::GetDirectoryName($outputLogPath)
$outputLogDirectory = [System.IO.Directory]::CreateDirectory($outputLogFolder)

.\MS2Create.exe $input $output $archiveName $mode $syncMode $logMode > $outputLogPath

pause