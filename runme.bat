@echo off

REM this is an example of use

MtpDownlaoder.exe -max-days 10 -c -l \Card\DCIM\Camera "\Phone\WhatsApp\Media\WhatsApp Images" -cp "%userprofile%\My Pictures\Foto" -rd -sp

MtpDownlaoder.exe -max-days 90 -delete \Card\DCIM\Camera
