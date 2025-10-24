import torch
import torch.nn as nn
from torchvision import transforms
from torchvision import models
from PIL import Image
import numpy as np
import sys
import json
import os

# Cấu hình stdout để sử dụng UTF-8
sys.stdout.reconfigure(encoding='utf-8')

# Định nghĩa danh sách màu và kiểu trình bày
COLORS = ['đỏ', 'đỏ đậm', 'đỏ tươi', 'đỏ cherry', 'đỏ rượu', 'đỏ hồng', 'h hồng', 'hồng nhạt', 'hồng đậm', 'hồng phấn',
          'hồng đào', 'hồng san hô', 'hồng cẩm chướng', 'hồng dâu', 'hồng phai', 'hồng sen', 'trắng', 'trắng kem', 'kem',
          'trắng ngọc trai', 'trắng sữa', 'vàng', 'vàng nhạt', 'vàng đậm', 'vàng cam', 'vàng cúc', 'vàng mù tạt',
          'vàng ánh kim', 'vàng đồng tiền', 'cam', 'cam cháy', 'cam đào', 'cam san hô', 'cam đất', 'cam rực', 'tím',
          'tím nhạt', 'tím violet', 'tím đậm', 'tím oải hương', 'tím lan', 'tím mộng mơ', 'tím hoàng gia', 'tím huệ',
          'xanh dương', 'xanh dương nhạt', 'xanh ngọc', 'xanh biển', 'xanh cobalt', 'xanh sapphire', 'xanh cẩm tú cầu',
          'xanh bạc hà', 'xanh lam', 'xanh lá', 'xanh lá nhạt', 'xanh olive', 'xanh rêu', 'xanh pastel', 'xanh đậm',
          'xanh lá mạ', 'nâu', 'nâu nhạt', 'nâu socola', 'nâu cà phê', 'nâu đất', 'xám', 'xám nhạt', 'đen']
PRESENTATIONS = ['Bó hoa', 'Hoa bó cổ điển', 'Giỏ hoa', 'Giỏ hoa hiện đại', 'Hộp hoa', 'Hộp hoa nghệ thuật',
                 'Lẵng hoa', 'Lẵng hoa mini', 'Bình hoa', 'Hoa để bàn']

# Định nghĩa mô hình
class FlowerModel(nn.Module):
    def __init__(self, num_colors, num_presentations):
        super(FlowerModel, self).__init__()
        self.resnet = models.resnet50(weights=None)  # Thay pretrained=False bằng weights=None
        self.resnet.fc = nn.Identity()
        self.fc = nn.Sequential(
            nn.Linear(2048, 512),
            nn.ReLU(),
            nn.Dropout(0.5),
            nn.Linear(512, 256),
            nn.ReLU(),
            nn.Dropout(0.5)
        )
        self.color_head = nn.Linear(256, num_colors)
        self.pres_head = nn.Linear(256, num_presentations)

    def forward(self, x):
        x = self.resnet(x)
        x = self.fc(x)
        color_out = torch.sigmoid(self.color_head(x))
        pres_out = self.pres_head(x)
        return color_out, pres_out

# Tiền xử lý ảnh
transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.ToTensor(),
    transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225])
])

def predict(image_path):
    try:
        device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        model = FlowerModel(num_colors=len(COLORS), num_presentations=len(PRESENTATIONS)).to(device)
        model_path = os.path.join(os.path.dirname(__file__), "..", "Models", "best_model.pth")
        model.load_state_dict(torch.load(model_path, map_location=device))
        model.eval()

        img = Image.open(image_path).convert('RGB')
        img = transform(img).unsqueeze(0).to(device)

        with torch.no_grad():
            color_out, pres_out = model(img)
        
        color_probs = color_out[0].cpu().numpy()
        top_color_indices = np.argsort(color_probs)[-4:][::-1]
        colors = [COLORS[i] for i in top_color_indices if color_probs[i] > 0.5]
        if not colors:
            colors = [COLORS[top_color_indices[0]]]

        pres_probs = torch.softmax(pres_out, dim=1)[0].cpu().numpy()
        presentation = PRESENTATIONS[np.argmax(pres_probs)]

        return {
            'success': True,
            'colors': colors,
            'presentation': presentation
        }
    except Exception as e:
        return {
            'success': False,
            'message': str(e)
        }

def main(image_path):
    result = predict(image_path)
    # Mã hóa JSON và in dưới dạng UTF-8
    json_str = json.dumps(result, ensure_ascii=False)
    print(json_str)
    return result

if __name__ == '__main__':
    if len(sys.argv) < 2:
        json_str = json.dumps({'success': False, 'message': 'Vui lòng cung cấp đường dẫn ảnh'}, ensure_ascii=False)
        print(json_str)
    else:
        main(sys.argv[1])