name: Bug 报告
description: 功能不正常、崩溃、译文异常等
labels: ["bug"]
body:
  - type: textarea
    id: describe
    attributes:
      label: 问题描述
      description: 发生了什么?期望发生什么?
      placeholder: 例如:开启实时翻译后,浏览器里的英文没有被翻译……
    validations:
      required: true
  - type: textarea
    id: repro
    attributes:
      label: 复现步骤
      placeholder: |
        1. 启动软件
        2. 点击……
        3. 出现……
    validations:
      required: true
  - type: textarea
    id: selftest
    attributes:
      label: selftest 输出
      description: 运行 `GlassTranslate.exe --selftest`,粘贴输出或 selftest.log 内容
      render: text
  - type: textarea
    id: errorlog
    attributes:
      label: error.log(如有)
      description: %APPDATA%\GlassTranslate\error.log 的内容(不含 API Key)
      render: text
  - type: input
    id: env
    attributes:
      label: 环境信息
      placeholder: "如:Windows 11 26300 / 144 DPI / 双显示器 / OpenAI 中转"
  - type: dropdown
    id: provider
    attributes:
      label: 使用的翻译服务商
      options:
        - OpenAI(官方)
        - OpenAI(中转/网关)
        - Anthropic(官方)
        - Anthropic(中转/网关)
        - 未配置 / 不确定
