#�����������PS��Ȩ�ޣ�������Ҫ�ֶ���PSִ��һ�Σ�
Set-ExecutionPolicy RemoteSigned

#ˢ������Dism�����б�
Write-Output "��ȡ��������Windows�����б�"
DISM /Online /Get-Features

#����Hyper-Vȫ���� + ��������ƽ̨ + ��ϵͳ֧��
Write-Output "����Hyper-Vȫ����ƽ̨"
DISM /Online /Enable-Feature /All /FeatureName:Microsoft-Hyper-V /NoRestart
DISM /Online /Enable-Feature /All /FeatureName:Microsoft-Hyper-V /NoRestart
DISM /Online /Enable-Feature /All /Featurename:VirtualMachinePlatform /NoRestart
DISM /Online /Enable-Feature /All /Featurename:HypervisorPlatform /NoRestart
DISM /Online /Enable-Feature /All /Featurename:Microsoft-Windows-Subsystem-Linux /NoRestart


#����Windows11�Դ�ɳ��
Write-Output "����Windows�Դ�SandBoxɳ��"
DISM /Online /Enable-Feature /All /Featurename:Containers-DisposableClientVM /NoRestart

#����WSL�����Զ���װ���а�
Write-Output "����WSL2��Linux��ϵͳ��������"
wsl --install --no-distribution
wsl --set-default-version 2
wsl --update

#NanaBox(Hyper-V�����������)
Write-Output "��װNanaBox(Hyper-V�����������)��UWPӦ�ã����ɰ�װ����ǰ���û���"
winget search --accept-source-agreements "9NJXJSCB2JK0"
winget install --force --accept-package-agreements --no-upgrade 9NJXJSCB2JK0

Write-Output "Hyper-Vȫ��Ͱ��WSL��װ��ϣ������������ǿ�ű��а�װWSL Manager(WSL���а������)"

pause