# EsmeraldaFileMover
Splits up incoming file bundles (xml and attachments) into different folders. The file bundles are created by e-services and distributed with Esmeralda. This service adapts the output from Esmeralda for easier consumption from Mule.

## Install
Create a folder on the server with EsmeraldaFileMover.exe and EsmeraldaFileMover.config. Update config file with the right paths. Install the service:

    sc.exe create EsmeraldaFileMover binPath= "<Path to EsmeraldaFileMover.exe>" start= auto
    sc.exe start EsmeraldaFileMover
    
## Uninstall
Run:

    sc.exe stop EsmeraldaFileMover
    sc.exe delete EsmeraldaFileMover
    
Remove EsmeraldaFileMover folder
