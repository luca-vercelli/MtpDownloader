#this can be used to compile under Linux. However the program will not work under Linux.

compile:
	mcs -reference:System.Drawing.dll -reference:bin/NDesk.Options.dll -reference:bin/MediaDevices.dll MtpDownloader/Program.cs MtpDownloader/FileSpec.cs MtpDownloader/Properties/AssemblyInfo.cs -out:bin/MtpDownloader.exe

wget:
	mkdir -p packages
	mkdir -p bin
	cd packages && wget https://www.nuget.org/api/v2/package/MediaDevices/1.7.0 -O mediadevices.1.7.0.nupkg
	cd packages && unzip -j mediadevices.1.7.0.nupkg lib/net45/MediaDevices.dll
	cd packages && wget https://www.nuget.org/api/v2/package/NDesk.Options/0.2.1 -O ndesk.options.0.2.1.nupkg
	cd packages && unzip -j ndesk.options.0.2.1.nupkg lib/NDesk.Options.dll
	mv packages/*.dll bin

clean:
	rm -rf packages
	rm -rf bin

