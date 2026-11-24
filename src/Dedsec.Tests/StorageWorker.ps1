param([Parameter(Mandatory)][string]$RequestPath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
function Hash-Text([string]$text){$sha=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}
function Disk-Key($disk){Hash-Text "$($disk.UniqueId)|$($disk.SerialNumber)|$($disk.Path)|$($disk.Size)"}
function Parts($disk){if([string]$disk.PartitionStyle -eq 'RAW'){return @()};@(Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object Offset)}
function Layout-Key($disk,$parts){Hash-Text (([string]$disk.PartitionStyle)+'|'+(($parts|ForEach-Object {"$($_.PartitionNumber):$($_.Offset):$($_.Size):$($_.GptType):$($_.Guid):$($_.IsBoot):$($_.IsSystem)"}) -join '|'))}
function Is-Protected($part){$g=([string]$part.GptType).Trim('{}').ToLowerInvariant();$part.IsBoot -or $part.IsSystem -or $g -in @('c12a7328-f81f-11d2-ba4b-00a0c93ec93b','e3c9e316-0b5c-4db8-817d-f92df00215ae','de94bba4-06d1-4d40-a16a-bfd50179d6ac') -or [string]$part.Type -in @('System','Reserved','Recovery')}
function Assert-Disk($req){
 $disk=Get-Disk -Number ([uint32]$req.disk) -ErrorAction Stop
 if((Disk-Key $disk) -cne $req.disk_key){throw 'Disk identity changed. Refresh before applying.'}
 $parts=@(Parts $disk)
 if((Layout-Key $disk $parts) -cne $req.layout_key){throw 'Partition layout changed. Refresh before applying.'}
 if($disk.IsOffline -or $disk.IsReadOnly -or $disk.IsClustered){throw 'Offline, read-only or clustered disk: operation refused.'}
 if($null -eq $disk.IsBoot -or $null -eq $disk.IsSystem){throw 'System disk state is unavailable.'}
 [pscustomobject]@{disk=$disk;parts=@($parts)}
}
function Assert-Part($req,$parts){$part=@($parts|Where-Object PartitionNumber -eq ([uint32]$req.partition));if($part.Count -ne 1 -or [uint64]$part[0].Offset -ne [uint64]$req.expected_offset -or [uint64]$part[0].Size -ne [uint64]$req.expected_size){throw 'Partition identity changed. Refresh before applying.'};$part[0]}
function Assert-DataPartition($part){
 if($null -eq $part.IsBoot -or $null -eq $part.IsSystem){throw 'Partition protection status unavailable.'};if(Is-Protected $part){throw 'Boot, system, EFI, recovery and reserved partitions cannot be formatted or deleted.'}
 $mounts=@($part.AccessPaths|Where-Object {$_ -match '^[A-Za-z]:\\'});$drive=([string]$part.DriveLetter).Trim([char]0);if($drive){$mounts+=($drive+':\')}
 $critical=@($env:SystemRoot,$env:USERPROFILE,$req.user_profile,$req.application_path,$req.data_path)+@(Get-CimInstance Win32_PageFileUsage -ErrorAction Stop|ForEach-Object {$_.Name})
 foreach($mount in $mounts){$prefix=[IO.Path]::GetFullPath($mount).TrimEnd('\')+'\';foreach($path in $critical){if($path){$resolved=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($path)).TrimEnd('\')+'\';if($resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Partition contains Windows, a user profile, a pagefile, this program or its operation journal.'}}}}
}
function Safe-Files([string]$directory){
 $pending=New-Object 'System.Collections.Generic.Queue[string]';$pending.Enqueue($directory);$files=New-Object 'System.Collections.Generic.List[object]';$visited=0
 while($pending.Count){$current=$pending.Dequeue();$visited++;if($visited -gt 5000){throw 'EFI directory count limit.'};foreach($entry in @(Get-ChildItem -LiteralPath $current -Force -ErrorAction Stop)){if(($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'EFI reparse point refused.'};if($entry.PSIsContainer){$pending.Enqueue($entry.FullName)}else{$files.Add($entry);if($files.Count -gt 50000){throw 'EFI file count limit.'}}}};$files.ToArray()
}
function Snapshot {
 $result=@();foreach($disk in @(Get-Disk -ErrorAction Stop | Sort-Object Number | Select-Object -First 256)){
  $parts=@();$errorText='';try{$native=@(Parts $disk);foreach($p in $native){$v=$null;try{$v=$p|Get-Volume -ErrorAction Stop}catch{};$parts+=@{number=[int]$p.PartitionNumber;offset=[uint64]$p.Offset;size=[uint64]$p.Size;gpt_type=[string]$p.GptType;type=[string]$p.Type;letter=([string]$p.DriveLetter).Trim([char]0);filesystem=[string]$v.FileSystemType;boot=[bool]$p.IsBoot;system=[bool]$p.IsSystem;protected=[bool](Is-Protected $p)}};$layout=Layout-Key $disk $native}catch{$errorText=$_.Exception.Message;$layout=''}
  $result+=@{number=[int]$disk.Number;key=Disk-Key $disk;layout_key=$layout;name=[string]$disk.FriendlyName;size=[uint64]$disk.Size;style=[string]$disk.PartitionStyle;boot=[bool]$disk.IsBoot;system=[bool]$disk.IsSystem;read_only=[bool]$disk.IsReadOnly;offline=[bool]$disk.IsOffline;clustered=[bool]$disk.IsClustered;error=$errorText;partitions=@($parts)}
 };@{disks=@($result);captured_utc=[DateTime]::UtcNow.ToString('o')}
}
function Efi-Folders([string]$root){
 $rows=@();if(!(Test-Path -LiteralPath $root)){return @()};foreach($dir in @(Get-ChildItem -LiteralPath $root -Directory -Force)){
  if(($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'EFI reparse point refused.'};$files=@(Safe-Files $dir.FullName)
  $signature=Hash-Text (($files|Sort-Object FullName|ForEach-Object {"$($_.FullName.Substring($dir.FullName.Length))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"}) -join "`n")
  $rows+=@{name=$dir.Name;bytes=[uint64](($files|Measure-Object Length -Sum).Sum);files=$files.Count;key=$signature;protected=($dir.Name -in @('Microsoft','Boot'))}
 };@($rows)
}
function With-Efi($req,$part,[scriptblock]$work){
 if(([string]$part.GptType).Trim('{}') -ine 'c12a7328-f81f-11d2-ba4b-00a0c93ec93b'){throw 'Selected partition is not GPT EFI.'}
 $volume=@($part|Get-Volume -ErrorAction Stop);if($volume.Count -ne 1 -or [string]$volume[0].FileSystemType -notin @('FAT','FAT32')){throw 'EFI cleanup supports only FAT/FAT32 partitions. Other filesystems are refused.'}
 $owned=$false;$letter=([string]$part.DriveLetter).Trim([char]0)
 if(!$letter){$used=@(Get-Volume|ForEach-Object {[string]$_.DriveLetter})+@($req.reserved_letters)+@(Get-PSDrive -PSProvider FileSystem|ForEach-Object {$_.Name});$letter=@('Z','Y','X','W','V','U','T','S','R'|Where-Object {$_ -notin $used})[0];if(!$letter){throw 'No free temporary drive letter.'};Add-PartitionAccessPath -DiskNumber $req.disk -PartitionNumber $req.partition -AccessPath ($letter+':\') -ErrorAction Stop;$owned=$true}
 try{$mounted=Get-Partition -DriveLetter ([char]$letter) -ErrorAction Stop;if($mounted.DiskNumber -ne $req.disk -or $mounted.PartitionNumber -ne $req.partition){throw 'EFI mount identity mismatch.'};$root=[IO.Path]::GetFullPath($letter+':\EFI');if((Test-Path -LiteralPath $root) -and ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'EFI root reparse point refused.'};& $work $root}finally{if($owned){Remove-PartitionAccessPath -DiskNumber $req.disk -PartitionNumber $req.partition -AccessPath ($letter+':\') -ErrorAction Stop}}
}
function Efi-Cleanup($req,[string]$root){
 $leaf=[string]$req.folder;if($leaf -notmatch '^[a-zA-Z0-9 _.-]{1,64}$' -or $leaf.TrimEnd(' ','.') -cne $leaf -or $leaf -in @('.','..','Microsoft','Boot')){throw 'Protected or invalid EFI folder.'}
 $folder=[IO.Path]::GetFullPath((Join-Path $root $leaf));$prefix=$root.TrimEnd('\')+'\';if(!$folder.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($folder) -ine $root){throw 'EFI path escapes selected directory.'}
 $before=@(Efi-Folders $root|Where-Object name -eq $leaf);if($before.Count -ne 1 -or $before[0].key -cne $req.folder_key){throw 'EFI directory changed. Inspect it again.'}
 $backup=Join-Path (Split-Path $OutputPath -Parent) 'efi-backup.zip';Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,$backup,[IO.Compression.CompressionLevel]::Optimal,$false)
 $archive=[IO.Compression.ZipFile]::OpenRead($backup);$manifest=@();try{foreach($file in @(Safe-Files $folder)){
  $relative=$file.FullName.Substring($folder.Length+1).Replace('\','/');$entry=$archive.GetEntry($relative);if(!$entry -or $entry.Length -ne $file.Length){throw 'EFI backup verification failed.'};$sha=[Security.Cryptography.SHA256]::Create();$stream=$entry.Open();try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','');$original=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;if($hash -ne $original){throw 'EFI backup hash mismatch.'};$manifest+=@{path=$relative;sha256=$original}}finally{$stream.Dispose();$sha.Dispose()}
 }}finally{$archive.Dispose()}
 $manifest|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path (Split-Path $OutputPath -Parent) 'efi-backup-files.json') -Encoding UTF8
 $after=@(Efi-Folders $root|Where-Object name -eq $leaf);if($after.Count -ne 1 -or $after[0].key -cne $req.folder_key){throw 'EFI source changed during backup. Nothing deleted.'}
 $finalFiles=@(Safe-Files $folder);if($finalFiles.Count -ne $manifest.Count){throw 'EFI source set changed before removal.'};foreach($file in $finalFiles){$relative=$file.FullName.Substring($folder.Length+1).Replace('\','/');$saved=@($manifest|Where-Object path -ceq $relative);if($saved.Count -ne 1 -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $saved[0].sha256){throw 'EFI content changed before removal. Nothing deleted.'}}
 Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction Stop
 if(Test-Path -LiteralPath $folder){throw 'EFI cleanup postcondition failed.'}
 @{message='Selected EFI directory removed after SHA256-verified backup.';backup=$backup}
}
function Boot-Inventory {
 $text=(& "$env:SystemRoot\System32\bcdedit.exe" /enum firmware /v 2>&1|Out-String);if($LASTEXITCODE -ne 0){throw $text};$rows=@()
 foreach($block in @([regex]::Split($text.Trim(),'(?:\r?\n){2,}'))){$match=[regex]::Match($block,'\{[0-9a-fA-F-]{36}\}');if(!$match.Success){continue};$id=$match.Value.ToLowerInvariant();$description=[regex]::Match($block,'(?im)^(?:description|описание)\s+(.+)$').Groups[1].Value.Trim();$rows+=@{id=$id;description=$description;detail=$block;protected=($id -in @('{9dea862c-5cdd-4e70-acc1-f32b344d4795}','{a5a30fa2-3d06-4e9f-b5f4-a01df9d1fcba}'))}}
 @{entries=@($rows);key=Hash-Text $text;raw=$text}
}
$req=$null
try{
 $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8|ConvertFrom-Json
 if($req.action -eq 'inventory'){$data=Snapshot}
 elseif($req.action -eq 'boot_inventory'){$data=Boot-Inventory}
 elseif($req.action -eq 'boot_delete'){
  $boot=Boot-Inventory;if($boot.key -cne $req.boot_key){throw 'Firmware entries changed. Refresh first.'};$id=[string]$req.entry_id
  if($id -notmatch '^\{[0-9a-fA-F-]{36}\}$'){throw 'Invalid BCD identifier.'};$entry=@($boot.entries|Where-Object id -eq $id);if($entry.Count -ne 1 -or $entry[0].protected){throw 'Protected or missing firmware object.'}
  $backup=Join-Path (Split-Path $OutputPath -Parent) 'system-store-backup.bcd';$boot.raw|Set-Content -LiteralPath (Join-Path (Split-Path $OutputPath -Parent) 'firmware-before.txt') -Encoding UTF8
  $export=(& "$env:SystemRoot\System32\bcdedit.exe" /export $backup 2>&1|Out-String);if($LASTEXITCODE -ne 0){throw $export}
  $deleted=(& "$env:SystemRoot\System32\bcdedit.exe" /delete $id 2>&1|Out-String);if($LASTEXITCODE -ne 0){throw $deleted};$after=Boot-Inventory;if(@($after.entries|Where-Object id -eq $id).Count){throw 'Firmware entry still appears after deletion.'};$data=@{message=$deleted;backup=$backup;note='BCD export does not provide a complete UEFI NVRAM restore; perform a normal restart to flush Windows BCD changes.'}
 }
 else{
  $checked=Assert-Disk $req;$disk=$checked.disk;$parts=@($checked.parts);$part=$null
  if($req.action -in @('resize','bounds','format','delete','letter','efi_inventory','efi_cleanup')){$part=Assert-Part $req $parts}
  if($req.action -notin @('bounds','efi_inventory')){Snapshot|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path (Split-Path $OutputPath -Parent) 'layout-before.json') -Encoding UTF8}
  switch($req.action){
   'initialize'{if([string]$disk.PartitionStyle -ne 'RAW' -or $parts.Count -gt 0 -or $disk.IsBoot -or $disk.IsSystem){throw 'Only an empty RAW non-system disk may be initialized.'};if($req.style -notin @('GPT','MBR')){throw 'Invalid partition style.'};Initialize-Disk -Number $disk.Number -PartitionStyle $req.style -Confirm:$false -ErrorAction Stop;if([string](Get-Disk -Number $req.disk).PartitionStyle -ne [string]$req.style){throw 'Initialization postcondition failed.'};$data=@{message='Disk initialized.'}}
   'bounds'{$supported=Get-PartitionSupportedSize -DiskNumber $req.disk -PartitionNumber $req.partition -ErrorAction Stop;$data=@{min=[uint64]$supported.SizeMin;max=[uint64]$supported.SizeMax}}
   'create'{
    [uint64]$offset=$req.offset;[uint64]$size=$req.bytes;if([string]$disk.PartitionStyle -notin @('GPT','MBR') -or $size -lt 1MB -or $offset -lt 1MB -or $offset%1MB -ne 0 -or $size%1MB -ne 0 -or $offset -gt $disk.Size -or $size -gt $disk.Size-$offset-1MB){throw 'Invalid aligned unallocated range.'}
    foreach($p in $parts){if($offset -lt $p.Offset+$p.Size -and $offset+$size -gt $p.Offset){throw 'Requested range overlaps a partition.'}}
    $created=New-Partition -DiskNumber $req.disk -Offset $offset -Size $size -AssignDriveLetter -ErrorAction Stop;$data=@{message='Partition created; not formatted.';partition=[int]$created.PartitionNumber}
   }
   'resize'{
    $type=([string]$part.GptType).Trim('{}');if((Is-Protected $part) -and !$part.IsBoot){throw 'Only data or running OS partitions may be resized; EFI/recovery/reserved refused.'}
    $supported=Get-PartitionSupportedSize -DiskNumber $req.disk -PartitionNumber $req.partition -ErrorAction Stop;[uint64]$size=$req.bytes;if($size -lt $supported.SizeMin -or $size -gt $supported.SizeMax -or $size%1MB -ne 0){throw 'Size is outside current Windows-supported bounds.'};Resize-Partition -DiskNumber $req.disk -PartitionNumber $req.partition -Size $size -Confirm:$false -ErrorAction Stop;if((Get-Partition -DiskNumber $req.disk -PartitionNumber $req.partition).Size -ne $size){throw 'Resize postcondition failed.'};$data=@{message='Partition resized.'}
   }
   'format'{Assert-DataPartition $part;if($req.filesystem -notin @('NTFS','exFAT','FAT32')){throw 'Unsupported filesystem.'};if(([string]$req.label).Length -gt 32 -or ([string]$req.label) -match '[\r\n]'){throw 'Invalid filesystem label.'};$formatOptions=@{Partition=$part;FileSystem=[string]$req.filesystem;Confirm=$false;Force=$true;ErrorAction='Stop'};if(([string]$req.label).Length){$formatOptions.NewFileSystemLabel=[string]$req.label};if($req.full){$formatOptions.Full=$true};Format-Volume @formatOptions|Out-Null;$formatted=Get-Partition -DiskNumber $req.disk -PartitionNumber $req.partition|Get-Volume;if([string]$formatted.FileSystemType -ine [string]$req.filesystem){throw 'Filesystem postcondition failed.'};$data=@{message='Volume formatted.'}}
   'delete'{Assert-DataPartition $part;Remove-Partition -DiskNumber $req.disk -PartitionNumber $req.partition -Confirm:$false -ErrorAction Stop;if(@(Get-Partition -DiskNumber $req.disk -PartitionNumber $req.partition -ErrorAction SilentlyContinue).Count){throw 'Delete postcondition failed.'};$data=@{message='Partition deleted.'}}
   'letter'{Assert-DataPartition $part;$letter=[string]$req.letter;if($letter -notmatch '^[D-Z]$' -or @(Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue).Count){throw 'Drive letter unavailable.'};Set-Partition -DiskNumber $req.disk -PartitionNumber $req.partition -NewDriveLetter ([char]$letter) -ErrorAction Stop;if([string](Get-Partition -DiskNumber $req.disk -PartitionNumber $req.partition).DriveLetter -ne $letter){throw 'Drive letter postcondition failed.'};$data=@{message='Drive letter changed.'}}
   'efi_inventory'{$data=With-Efi $req $part {param($root)@{folders=@(Efi-Folders $root);note='No folder is automatically classified as unused.'}}}
   'efi_cleanup'{$data=With-Efi $req $part {param($root)Efi-Cleanup $req $root}}
   default{throw 'Unsupported storage operation.'}
  }
 }
 @{success=$true;operation_id=$req.operation_id;data=$data;error=''}|ConvertTo-Json -Depth 12 -Compress|Set-Content -LiteralPath $OutputPath -Encoding UTF8
}catch{@{success=$false;operation_id=$req.operation_id;data=$null;error=$_.Exception.Message}|ConvertTo-Json -Depth 4 -Compress|Set-Content -LiteralPath $OutputPath -Encoding UTF8;exit 1}
