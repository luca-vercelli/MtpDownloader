@echo off

REM this is an example of use

MtpDownloader.exe -max-days 10 -c -l \Card\DCIM\Camera "\Phone\WhatsApp\Media\WhatsApp Images" -cp "%userprofile%\Pictures\Foto" -rd -sp

MtpDownloader.exe -max-days 90 -delete \Card\DCIM\Camera
