#允许本地运行PS的权限（可能需要手动打开PS执行一次）
Set-ExecutionPolicy RemoteSigned

#刷新在线Dism组件列表
DISM /Online /Get-Capabilities

#启用无线显示器
DISM /Online /Add-Capability /CapabilityName:App.WirelessDisplay.Connect

#启用简体中文语言包
DISM /Online /Add-Capability /CapabilityName:Language.Basic~~~zh-CN~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Fonts.Hans~~~und-HANS~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Handwriting~~~zh-CN~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.OCR~~~zh-CN~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Speech~~~zh-CN~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.TextToSpeech~~~zh-CN~0.0.1.0
#启用日文字体补充包
DISM /Online /Add-Capability /CapabilityName:Language.Fonts.Jpan~~~und-JPAN~0.0.1.0

echo "安装【无线显示器】+【简体中文补充包】完成，是否继续安装繁体语言包（HK/TW）"
pause

#启用繁体中文（香港）语言包
DISM /Online /Add-Capability /CapabilityName:Language.Basic~~~zh-HK~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Fonts.Hant~~~und-HANT~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Handwriting~~~zh-HK~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.OCR~~~zh-HK~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Speech~~~zh-HK~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.TextToSpeech~~~zh-HK~0.0.1.0

#启用繁体中文（台湾）语言包
DISM /Online /Add-Capability /CapabilityName:Language.Basic~~~zh-TW~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Fonts.Hant~~~und-HANT~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Handwriting~~~zh-TW~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.OCR~~~zh-TW~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.Speech~~~zh-TW~0.0.1.0
DISM /Online /Add-Capability /CapabilityName:Language.TextToSpeech~~~zh-TW~0.0.1.0


pause