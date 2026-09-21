#�����������PS��Ȩ�ޣ�������Ҫ�ֶ���PSִ��һ�Σ�
Set-ExecutionPolicy RemoteSigned

# #��װChocolatey(choco)��������
# echo "�����Թٷ��ű���װChocolatey(choco)��������"
# iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

# #��װ����΢��Edge����������壩
# echo "���ڰ�װ΢��Edge����������壩"
# Start-Process -FilePath ".\���������MicrosoftEdgeSetup΢�������_zh-CN.exe"

# #��װ���عȸ�Chrome����������壩
# echo "���ڰ�װ�ȸ�Chrome����������壩"
# Start-Process -FilePath ".\���������ChromeSetup�ȸ������_zh-HK.exe"


# #����Microsoft.VisualStudioCode���IDE����
# echo "�����������õ�Visual Studio Code��װ��"
# winget search --accept-source-agreements "Microsoft.VisualStudioCode"
# #��װVisual Studio Code
# echo "���ڰ�װVisual Studio Code"
# winget install --force --accept-package-agreements --no-upgrade Microsoft.VisualStudioCode --scope machine

# #��װ����PureCodec��������
# echo "���ڰ�װPureCodec��������"
# Start-Process -FilePath ".\���칫Ӱ���PureCodec��������_v2024.09.24.exe" -ArgumentList "/S /D=C:\Progra~1\PureCodec"

# #��װ����2345��ͼ����ȥ���棩
# echo "���ڰ�װ2345��ͼ����ȥ���棩"
# Start-Process -FilePath ".\���칫Ӱ�����ͼ��_ȥ����_v11.4.1.10796_mefcl_x64_Setup_v1.exe" -ArgumentList "/S /D=C:\Progra~1\2345Pic"


# #����PixPin��ͼ����
# echo "�����������õ�PixPin��װ��"
# winget search --accept-source-agreements "PixPin.PixPin"
# #��װPixPin
# echo "���ڰ�װPixPin������װ����ǰ���û���"
# winget install --force --accept-package-agreements --no-upgrade PixPin.PixPin.Beta

# #��װ����imFile������
# echo "���ڰ�װimFile�������������NDM��������imFileĿ¼"
# Start-Process -FilePath ".\�������ϴ���imFile(���Aria2-Explorer�����RPC16800)-v1.1.2.exe" -ArgumentList "/S /D=C:\Progra~1\imFile"
# Copy-Item ".\�������ϴ���NDM���߳�������_���ļ�_v1.4.10.exe" -Destination "C:\Progra~1\imFile"

# #����ThisPC��IE��������ļ��������ֵ������ݷ�ʽ
# echo "���ڴ���ThisPC��IE��������ļ��������ֵ������ݷ�ʽ"
# Copy-Item ".\�����������Internet Explorer for Windows 11.lnk" -Destination "C:\Users\Public\Desktop"
# Copy-Item ".\�����������This PC.lnk" -Destination "C:\Users\Public\Desktop"
# Copy-Item ".\�������ϴ���΢���ļ���������(��ҳ��).url" -Destination "C:\Users\Public\Desktop"
# Copy-Item ".\�����������Windows Store.url" -Destination "C:\Users\Public\Desktop"


# #����Everything�ļ���������
# echo "�����������õ�Everything��װ��"
# winget search --accept-source-agreements "voidtools.Everything"
# #��װEverything
# echo "���ڰ�װEverything"
# winget install --force --accept-package-agreements --no-upgrade voidtools.Everything.Alpha --scope machine

# #��װ����Bandizip-Proѹ��������˴��༶Ŀ¼����ν����װ������ʶ��
# echo "���ڰ�װBandizip-Proѹ�����"
# Start-Process -FilePath ".\��ѹ�������Bandizip-Pro-v7.36-x64-Repack.exe" -ArgumentList '/ai /gm2 /InstallPath="C:\Progra~1\BandiZip"'

# #��װImgDrive-Pro������ع��ߣ�Ϊ������װOffice�����׼����
# echo "�ͷ�ImgDrive��C��C:\Program FilesĿ¼"
# Expand-Archive -Force -Path ".\���칫Ӱ���ImgDrive-Pro_v2.1.9.zip" -DestinationPath "C:\Progra~1\"
# echo "���ImgDrive���㽫���Թ���ISO�͸���IMG����"
# Start-Process -FilePath "C:\Progra~1\ImgDrive-Pro\imgdrive.exe"
# echo "����ImgDrive��ݷ�ʽ"
# $shell = New-Object -ComObject WScript.Shell
# $shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\ImgDrive.lnk")
# $shortcut.TargetPath = "C:\Progra~1\ImgDrive-Pro\imgdrive.exe"  # ���ÿ�ݷ�ʽĿ�����
# $shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
# $shortcut.WorkingDirectory = "C:\Progra~1\ImgDrive-Pro\"  # ���á���ʼλ�á����ԣ�������Ŀ¼
# $shortcut.IconLocation = "C:\Progra~1\ImgDrive-Pro\imgdrive.exe,0"  # ��ݷ�ʽͼ��
# $shortcut.Save()


# #��������Sysdiag��ȫ���
# echo "�����������õĻ��ް�ȫ�����װ��"
# winget search --accept-source-agreements "����"
# #��װ���ް�ȫ���Sysdiag
# echo "���ڰ�װ���ް�ȫ���Sysdiag�����û���װ��ȫ�ֿ��ã�"
# winget install --force --accept-package-agreements --no-upgrade XPDNH1FMW7NB40
# echo "������������Զ����·���"
# sleep 1s
# Start-Process -FilePath "C:\Progra~1\Huorong\Sysdiag\bin\HRUpdate.exe"

# #��װ����SouGou�ѹ����뷨
# echo "���ڰ�װSouGou�ѹ����뷨"
# Start-Process -FilePath ".\�����뷨���ѹ����뷨_ȥ���_�����Ż���_v14.9.0.9966.exe" -ArgumentList "/S /D=C:\Progra~1\SogouInput"

# #��װ���ظ�ǿ���FlClash��Ϊ������װGithub�����׼����
# echo "�ͷ�FlClash-Proxy��C��C:\Program FilesĿ¼"
# Expand-Archive -Force -Path ".\����ǿ���ߡ�FlClash-Proxy_v0.8.66.zip" -DestinationPath "C:\Progra~1\"
# echo "���FlClash����Ӧ��ȥ��ӽڵ㣬�Ա��������װ����Github�����������"
# Start-Process -FilePath "C:\Progra~1\FlClash-Proxy\FlClash.exe"
# echo "����FlClash��ݷ�ʽ"
# $shell = New-Object -ComObject WScript.Shell
# $shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\FlClash.lnk")
# $shortcut.TargetPath = "C:\Progra~1\FlClash-Proxy\FlClash.exe"  # ���ÿ�ݷ�ʽĿ�����
# $shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
# $shortcut.WorkingDirectory = "C:\Progra~1\FlClash-Proxy\"  # ���á���ʼλ�á����ԣ�������Ŀ¼
# $shortcut.IconLocation = "C:\Progra~1\FlClash-Proxy\FlClash.exe,0"  # ��ݷ�ʽͼ��
# $shortcut.Save()


#��װ��������Github���������������ص����
echo ""
echo "������װ��������Github���������������ص����"
echo "WinGetĬ��ʹ�ô���127.0.0.1:7890����ȷ���ѿ�������"
echo "�����밴�������������رմ���"
echo ""
pause

# #����WinGet�Ĵ������
# winget settings --enable ProxyCommandLineOptions

# #����chocolatey.chocolatey��������
# echo "�����������õ�Chocolatey(choco)����������װ��"
# winget search --accept-source-agreements "chocolatey.chocolatey"
# #��װChocolatey(choco)��������
# echo "���ڰ�װChocolatey(choco)��������"
# winget install --force --accept-package-agreements --no-upgrade chocolatey.chocolatey --scope machine --proxy http://127.0.0.1:7890
# choco upgrade chocolatey

# #����PeaZip
# echo "�����������õ�PeaZip��װ��"
# winget search --accept-source-agreements "Giorgiotani.Peazip"
# #��װPeaZip
# echo "���ڰ�װPeaZip"
# winget install --force --accept-package-agreements --no-upgrade Giorgiotani.Peazip --scope machine --proxy http://127.0.0.1:7890

# #����LocalSend
# echo "�����������õ�LocalSend��װ��"
# winget search --accept-source-agreements "LocalSend"
# #��װLocalSend
# echo "���ڰ�װLocalSend"
# winget install --force --accept-package-agreements --no-upgrade LocalSend.LocalSend --scope machine --proxy http://127.0.0.1:7890

# #����Syncthing
# echo "�����������õ�Syncthing��װ��"
# winget search --accept-source-agreements "Syncthing"
# #��װSyncthing
# echo "���ڰ�װSyncthing��tray����ͼ��"
# winget install --force --accept-package-agreements --no-upgrade Syncthing.Syncthing --scope machine --proxy http://127.0.0.1:7890
# winget install --force --accept-package-agreements --no-upgrade Martchus.syncthingtray --scope machine --proxy http://127.0.0.1:7890


echo ""
echo "�����WinGet��Chocolatey��������İ�װ"
echo ""
pause