# -*- coding: utf-8 -*-
import json, os

OUT = os.path.join(os.path.dirname(__file__), "examples")
os.makedirs(OUT, exist_ok=True)

SVG = {}

SVG["A"] = '''<svg width="100%" height="100%" viewBox="0 0 120 170" xmlns="http://www.w3.org/2000/svg">
  <text x="60" y="14" font-size="10" text-anchor="middle" fill="#94a3b8">{label}</text>
  <rect x="10" y="22" width="100" height="130" rx="10" fill="#0f172a" stroke="{inactiveColor}" stroke-width="3"/>
  <svg x="20" y="32" width="80" height="110" viewBox="0 0 80 110" preserveAspectRatio="none">
    <g transform="translate(0,110) scale(1,-1)">
      <rect x="0" y="0" width="80" height="{normalizedPercent}%" fill="{activeColor}"/>
    </g>
  </svg>
  <text x="60" y="164" font-size="{fontSize}" text-anchor="middle" fill="#e2e8f0">{value}{unit}</text>
</svg>'''

SVG["B"] = '''<svg width="100%" height="100%" viewBox="0 0 200 40" xmlns="http://www.w3.org/2000/svg">
  <rect x="0" y="14" width="200" height="12" rx="6" fill="#1e293b" stroke="{inactiveColor}" stroke-width="2"/>
  <line x1="10" y1="20" x2="190" y2="20" stroke="{activeColor}" stroke-width="6" stroke-linecap="round" stroke-dasharray="14 12">
    <animate attributeName="stroke-dashoffset" from="0" to="-26" dur="0.8s" repeatCount="indefinite"/>
  </line>
</svg>'''

SVG["C"] = '''<svg width="100%" height="100%" viewBox="0 0 120 120" xmlns="http://www.w3.org/2000/svg">
  <circle cx="60" cy="60" r="48" fill="none" stroke="#1e293b" stroke-width="12"/>
  <circle cx="60" cy="60" r="48" fill="none" stroke="{alertColor}" stroke-width="12"
          pathLength="100" stroke-dasharray="{normalizedPercent} 100"
          transform="rotate(-90 60 60)" stroke-linecap="round"/>
  <text x="60" y="58" font-size="{fontSize}" text-anchor="middle" fill="#e2e8f0">{value}{unit}</text>
  <text x="60" y="74" font-size="9" text-anchor="middle" fill="#94a3b8">{label}</text>
</svg>'''

SVG["D"] = '''<svg width="100%" height="100%" viewBox="0 0 200 36" xmlns="http://www.w3.org/2000/svg">
  <rect x="4" y="10" width="192" height="16" rx="8" fill="#1e293b" stroke="{inactiveColor}" stroke-width="1.5"/>
  <svg x="4" y="10" width="192" height="16" viewBox="0 0 192 16" preserveAspectRatio="none">
    <rect x="0" y="0" width="{normalizedPercent}%" height="16" rx="8" fill="{alertColor}"/>
  </svg>
  <text x="100" y="32" font-size="9" text-anchor="middle" fill="#94a3b8">{label}</text>
</svg>'''

SVG["E"] = '''<svg width="100%" height="100%" viewBox="0 0 80 40" xmlns="http://www.w3.org/2000/svg">
  <circle cx="18" cy="20" r="12" fill="{alertColor}">
    <animate attributeName="opacity" values="1;0.4;1" dur="1.2s" repeatCount="indefinite"/>
  </circle>
  <text x="38" y="25" font-size="{fontSize}" fill="#e2e8f0">{state}</text>
  <text x="38" y="14" font-size="9" fill="#94a3b8">{label}</text>
  <text x="72" y="14" font-size="9" fill="#ef4444">{quality}</text>
</svg>'''

SVG["F"] = '''<svg width="100%" height="100%" viewBox="0 0 60 160" xmlns="http://www.w3.org/2000/svg">
  <rect x="24" y="20" width="12" height="110" rx="6" fill="#1e293b" stroke="{inactiveColor}" stroke-width="2"/>
  <circle cx="30" cy="140" r="14" fill="{alertColor}" stroke="{inactiveColor}" stroke-width="2"/>
  <g transform="translate(0,130) scale(1,0.011) scale(1,{normalizedPercent})">
    <rect x="26" y="-100" width="8" height="100" rx="4" fill="{alertColor}"/>
  </g>
  <text x="30" y="14" font-size="{fontSize}" text-anchor="middle" fill="#e2e8f0">{value}{unit}</text>
</svg>'''

SVG["G"] = '''<svg width="100%" height="100%" viewBox="0 0 200 90" xmlns="http://www.w3.org/2000/svg">
  <rect x="4" y="4" width="192" height="82" rx="10" fill="#0f172a" stroke="{inactiveColor}" stroke-width="1.5"/>
  <rect x="4" y="4" width="6" height="82" rx="3" fill="{activeColor}"/>
  <text x="20" y="30" font-size="11" fill="#94a3b8">{label}</text>
  <text x="20" y="62" font-size="26" fill="#e2e8f0">{value}</text>
  <text x="186" y="62" font-size="12" text-anchor="end" fill="#94a3b8">{unit}</text>
</svg>'''

SVG["J"] = '''<svg width="100%" height="100%" viewBox="0 0 120 60" xmlns="http://www.w3.org/2000/svg">
  <rect x="10" y="14" width="96" height="32" rx="4" fill="none" stroke="{inactiveColor}" stroke-width="3"/>
  <rect x="106" y="24" width="6" height="12" rx="2" fill="{inactiveColor}"/>
  <svg x="14" y="18" width="88" height="24" viewBox="0 0 88 24" preserveAspectRatio="none">
    <rect x="0" y="0" width="{normalizedPercent}%" height="24" fill="{alertColor}"/>
  </svg>
  <text x="60" y="56" font-size="11" text-anchor="middle" fill="#e2e8f0">{value}{unit}</text>
</svg>'''

SVG["K"] = '''<svg width="100%" height="100%" viewBox="0 0 220 30" xmlns="http://www.w3.org/2000/svg">
  <rect x="4" y="6" width="212" height="18" rx="4" fill="#0f172a" stroke="{alertColor}" stroke-width="2"/>
  <polygon points="14,10 22,22 6,22" fill="{alertColor}"/>
  <text x="30" y="19" font-size="11" fill="{alertColor}">{label}</text>
</svg>'''

# 通用 svg 轨默认属性
def svg_props(unit="%"):
    return {
        "activeColor": "#3b82f6",
        "inactiveColor": "#94a3b8",
        "minValue": 0,
        "maxValue": 100,
        "unit": unit,
        "fontSize": 12,
        "thresholdMin": 10,
        "thresholdMax": 90,
        "onText": "开启",
        "offText": "关闭",
    }

# 通用 svg 轨 schema（键与 defaultProps 对齐）
def svg_schema(extra=None):
    base = [
        {"key": "activeColor", "label": "运行色", "type": "color"},
        {"key": "inactiveColor", "label": "底色", "type": "color"},
        {"key": "minValue", "label": "量程下限", "type": "number", "default": 0},
        {"key": "maxValue", "label": "量程上限", "type": "number", "default": 100},
        {"key": "unit", "label": "单位", "type": "text", "placeholder": "e.g. %"},
        {"key": "thresholdMax", "label": "高限报警", "type": "number", "nullable": True},
        {"key": "thresholdMin", "label": "低限预警", "type": "number", "nullable": True},
        {"key": "fontSize", "label": "字号", "type": "number", "min": 8, "max": 72},
        {"key": "onText", "label": "开启文本", "type": "text", "default": "开启"},
        {"key": "offText", "label": "关闭文本", "type": "text", "default": "关闭"},
    ]
    if extra:
        base = extra + base
    return base

templates = []

# A 竖式液位罐
templates.append({
    "id": 0, "templateKey": "my-tank-level", "renderType": "my-tank-level",
    "name": "竖式液位罐", "category": "equipment",
    "description": "SVG轨示例：嵌套svg + 百分比实现底部上涨液位（技巧1）",
    "defaultWidth": 120, "defaultHeight": 170,
    "iconKind": "emoji", "iconKey": "💧", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["A"],
    "defaultPropsJson": svg_props("%"),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# B 流向管道
templates.append({
    "id": 0, "templateKey": "my-pipe-flow-h", "renderType": "my-pipe-flow-h",
    "name": "流向管道", "category": "structures",
    "description": "SVG轨示例：animate + dasharray 实现流动虚线（技巧2 + <animate>）",
    "defaultWidth": 200, "defaultHeight": 40,
    "iconKind": "emoji", "iconKey": "〰️", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["B"],
    "defaultPropsJson": svg_props(""),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# C 环形进度仪表
templates.append({
    "id": 0, "templateKey": "my-radial-gauge", "renderType": "my-radial-gauge",
    "name": "环形进度仪表", "category": "sensors",
    "description": "SVG轨示例：pathLength + stroke-dasharray 环形进度（技巧2）",
    "defaultWidth": 120, "defaultHeight": 120,
    "iconKind": "emoji", "iconKey": "🎯", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["C"],
    "defaultPropsJson": svg_props("%"),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# D 水平进度条
templates.append({
    "id": 0, "templateKey": "my-linear-bar", "renderType": "my-linear-bar",
    "name": "水平进度条", "category": "sensors",
    "description": "SVG轨示例：嵌套svg + 百分比宽度进度条",
    "defaultWidth": 200, "defaultHeight": 36,
    "iconKind": "emoji", "iconKey": "📊", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["D"],
    "defaultPropsJson": svg_props("%"),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# E 状态指示灯
templates.append({
    "id": 0, "templateKey": "my-status-lamp", "renderType": "my-status-lamp",
    "name": "状态指示灯", "category": "sensors",
    "description": "SVG轨示例：alertColor 状态色 + quality 条件显示（技巧5/6）",
    "defaultWidth": 80, "defaultHeight": 40,
    "iconKind": "emoji", "iconKey": "💡", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["E"],
    "defaultPropsJson": svg_props(""),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# F 温度计
templates.append({
    "id": 0, "templateKey": "my-thermometer", "renderType": "my-thermometer",
    "name": "温度计", "category": "sensors",
    "description": "SVG轨示例：嵌套scale做单位换算（技巧4）+ alertColor",
    "defaultWidth": 60, "defaultHeight": 160,
    "iconKind": "emoji", "iconKey": "🌡️", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["F"],
    "defaultPropsJson": svg_props("℃"),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# G 数据卡片
templates.append({
    "id": 0, "templateKey": "my-mini-card", "renderType": "my-mini-card",
    "name": "数据卡片", "category": "sensors",
    "description": "SVG轨示例：文本组合 value/unit/label 的极简看板卡",
    "defaultWidth": 200, "defaultHeight": 90,
    "iconKind": "emoji", "iconKey": "🪧", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["G"],
    "defaultPropsJson": svg_props(""),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# J 电池电量
templates.append({
    "id": 0, "templateKey": "my-battery", "renderType": "my-battery",
    "name": "电池电量", "category": "sensors",
    "description": "SVG轨示例：百分比宽度 + alertColor 变色电量格",
    "defaultWidth": 120, "defaultHeight": 60,
    "iconKind": "emoji", "iconKey": "🔋", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["J"],
    "defaultPropsJson": svg_props("%"),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# K 预警条
templates.append({
    "id": 0, "templateKey": "my-warning-bar", "renderType": "my-warning-bar",
    "name": "预警条", "category": "headers",
    "description": "SVG轨示例：alertColor 边框 + 文本随阈值/质量变色",
    "defaultWidth": 220, "defaultHeight": 30,
    "iconKind": "emoji", "iconKey": "⚠️", "iconColor": "",
    "renderKind": "svg", "svgTemplate": SVG["K"],
    "defaultPropsJson": svg_props(""),
    "propSchemaJson": svg_schema(),
    "isSystem": False, "sortOrder": 300,
})

# H builtin 预设：罐体变体（复用 tank SFC）
templates.append({
    "id": 0, "templateKey": "my-tank-preset", "renderType": "tank",
    "name": "清水储罐(预设)", "category": "equipment",
    "description": "builtin轨预设变体：复用 tank SFC + 自定义默认参数，schema留空自动用内置",
    "defaultWidth": 120, "defaultHeight": 160,
    "iconKind": "emoji", "iconKey": "🛢️", "iconColor": "",
    "renderKind": "builtin", "svgTemplate": None,
    "defaultPropsJson": {
        "activeColor": "#0ea5e9", "fillColor": "#38bdf8", "inactiveColor": "#94a3b8",
        "minValue": 0, "maxValue": 50, "unit": "m³",
        "thresholdMax": 45, "thresholdMin": 5, "showLabel": True,
    },
    "propSchemaJson": [],
    "isSystem": False, "sortOrder": 320,
})

# I builtin 预设：温度表盘（复用 gauge-dial SFC）
templates.append({
    "id": 0, "templateKey": "my-gauge-temp", "renderType": "gauge-dial",
    "name": "温度表盘(预设)", "category": "sensors",
    "description": "builtin轨预设变体：复用 gauge-dial SFC，量程-20~150℃，红色超温",
    "defaultWidth": 120, "defaultHeight": 120,
    "iconKind": "emoji", "iconKey": "🌡️", "iconColor": "",
    "renderKind": "builtin", "svgTemplate": None,
    "defaultPropsJson": {
        "activeColor": "#ef4444", "inactiveColor": "#94a3b8",
        "minValue": -20, "maxValue": 150, "unit": "℃",
        "thresholdMax": 120, "thresholdMin": -10,
    },
    "propSchemaJson": [],
    "isSystem": False, "sortOrder": 320,
})

letter = {"A":"A-my-tank-level","B":"B-my-pipe-flow-h","C":"C-my-radial-gauge",
          "D":"D-my-linear-bar","E":"E-my-status-lamp","F":"F-my-thermometer",
          "G":"G-my-mini-card","H":"H-my-tank-preset","I":"I-my-gauge-temp",
          "J":"J-my-battery","K":"K-my-warning-bar"}

# 关键：API 契约要求 defaultPropsJson / propSchemaJson 是「JSON 字符串」，
# 管理页导入时用 String(... ?? '{}') 强制转换，嵌套对象会变成 "[object Object]" 而损坏。
for t in templates:
    t["defaultPropsJson"] = json.dumps(t["defaultPropsJson"], ensure_ascii=False)
    t["propSchemaJson"] = json.dumps(t["propSchemaJson"], ensure_ascii=False)
    # 找到对应 letter 前缀
    pref = [k for k,v in letter.items() if t["templateKey"].endswith(v.split("-",1)[1]) or v==t["templateKey"]]
    out_name = pref[0] + "-" + t["templateKey"] if pref else t["templateKey"]
    doc = {
        "format": "scada-widget-template",
        "version": 1,
        "template": t,
    }
    path = os.path.join(OUT, out_name + ".widget.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
    print("written:", path)

print("total:", len(templates))
