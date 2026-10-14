# RapidOCR 模型

PaddleOCR 模型的 ONNX 版本，来自 RapidOCR v3.10.0 默认模型清单（[default_models.yaml](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml)），许可证 Apache-2.0。

| 文件 | 用途 | 来源 | SHA256 |
| --- | --- | --- | --- |
| `PP-OCRv6_det_small.onnx` | 文字检测 | ModelScope `RapidAI/RapidOCR` v3.10.0 `onnx/PP-OCRv6/det/` | `090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f` |
| `PP-OCRv6_rec_small.onnx` | 文字识别（中、繁、日与拉丁字母语言） | ModelScope `RapidAI/RapidOCR` v3.10.0 `onnx/PP-OCRv6/rec/` | `6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884` |
| `ppocrv6_dict.txt` | 识别字典，必须与识别模型配套 | ModelScope `RapidAI/RapidOCR` v3.10.0 `paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/` | `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d` |
| `ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx` | 文本行 180° 方向分类（PP-OCRv6 沿用 v5 分类器） | NuGet `RapidOcrNet` 4.2.0 自带 `models/v5/` | `54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7` |

更换模型时要同时更换识别模型与字典，并按 `RapidOcrEngine` 注释中的对比测试结论重新评估检测参数。
