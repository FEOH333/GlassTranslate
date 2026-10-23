name: 功能建议
description: 新功能或改进想法
labels: ["enhancement"]
body:
  - type: textarea
    id: idea
    attributes:
      label: 你想要什么功能?
      description: 描述使用场景,而不只是实现方式——也许有更好的做法。
    validations:
      required: true
  - type: textarea
    id: alt
    attributes:
      label: 现在的替代方案
      description: 目前你是怎么凑合解决这个需求的?
  - type: dropdown
    id: area
    attributes:
      label: 涉及方面
      multiple: true
      options:
        - 悬浮球 / 面板 UI
        - 译文气泡 / 摆位
        - 翻译质量 / 提示词
        - API 与成本
        - OCR / 性能
        - 快捷键
        - 其他
