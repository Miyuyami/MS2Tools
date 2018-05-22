# args for MS2Extract
$input = "C:\Games\MapleStory 2\MapleStory2\Data\Resource\Image"
$output = "C:\MS2ExtractOutput"
$syncMode = "0"
$logMode = "3"

$logFileName = "output.log"
$outputLogPath = [System.IO.Path]::Combine($output, $logFileName)

# we need to manually create this so that powershell won't throw an exception if the directory doesn't exist
$outputLogFolder = [System.IO.Path]::GetDirectoryName($outputLogPath)
$outputLogDirectory = [System.IO.Directory]::CreateDirectory($outputLogFolder)

.\MS2Extract.exe $input $output $syncMode $logMode > $outputLogPath

pause