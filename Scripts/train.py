import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from torchvision import models, transforms
from PIL import Image
import pandas as pd
import numpy as np
import os
import sys

# Định nghĩa danh sách màu và kiểu trình bày
COLORS = ['đỏ', 'đỏ đậm', 'đỏ tươi', 'đỏ cherry', 'đỏ rượu', 'đỏ hồng', 'hồng', 'hồng nhạt', 'hồng đậm', 'hồng phấn',
          'hồng đào', 'hồng san hô', 'hồng cẩm chướng', 'hồng dâu', 'hồng phai', 'hồng sen', 'trắng', 'trắng kem', 'kem',
          'trắng ngọc trai', 'trắng sữa', 'vàng', 'vàng nhạt', 'vàng đậm', 'vàng cam', 'vàng cúc', 'vàng mù tạt',
          'vàng ánh kim', 'vàng đồng tiền', 'cam', 'cam cháy', 'cam đào', 'cam san hô', 'cam đất', 'cam rực', 'tím',
          'tím nhạt', 'tím violet', 'tím đậm', 'tím oải hương', 'tím lan', 'tím mộng mơ', 'tím hoàng gia', 'tím huệ',
          'xanh dương', 'xanh dương nhạt', 'xanh ngọc', 'xanh biển', 'xanh cobalt', 'xanh sapphire', 'xanh cẩm tú cầu',
          'xanh bạc hà', 'xanh lam', 'xanh lá', 'xanh lá nhạt', 'xanh olive', 'xanh rêu', 'xanh pastel', 'xanh đậm',
          'xanh lá mạ', 'nâu', 'nâu nhạt', 'nâu socola', 'nâu cà phê', 'nâu đất', 'xám', 'xám nhạt', 'đen']
PRESENTATIONS = ['Bó hoa', 'Hoa bó cổ điển', 'Giỏ hoa', 'Giỏ hoa hiện đại', 'Hộp hoa', 'Hộp hoa nghệ thuật',
                 'Lẵng hoa', 'Lẵng hoa mini', 'Bình hoa', 'Hoa để bàn']

# Định nghĩa dataset
class FlowerDataset(Dataset):
    def __init__(self, csv_file, root_dir, transform=None):
        self.data = pd.read_csv(os.path.join(root_dir, csv_file))
        self.root_dir = root_dir
        self.transform = transform
        self.color_to_idx = {color: idx for idx, color in enumerate(COLORS)}
        self.pres_to_idx = {pres: idx for idx, pres in enumerate(PRESENTATIONS)}

    def __len__(self):
        return len(self.data)

    def __getitem__(self, idx):
        img_path = os.path.join(self.root_dir, self.data.iloc[idx, 0])
        image = Image.open(img_path).convert('RGB')
        colors = self.data.iloc[idx, 1].split(',')
        presentation = self.data.iloc[idx, 2]

        # Chuyển nhãn màu thành vector nhị phân (multi-label)
        color_labels = np.zeros(len(COLORS))
        for color in colors:
            if color in self.color_to_idx:
                color_labels[self.color_to_idx[color]] = 1

        # Chuyển nhãn kiểu trình bày thành chỉ số
        pres_label = self.pres_to_idx[presentation]

        if self.transform:
            image = self.transform(image)

        return image, torch.tensor(color_labels, dtype=torch.float), torch.tensor(pres_label, dtype=torch.long)

# Định nghĩa mô hình
class FlowerModel(nn.Module):
    def __init__(self, num_colors, num_presentations):
        super(FlowerModel, self).__init__()
        self.resnet = models.resnet50(pretrained=True)
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
train_transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.RandomHorizontalFlip(),
    transforms.RandomRotation(10),
    transforms.ToTensor(),
    transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225])
])

val_transform = transforms.Compose([
    transforms.Resize((224, 224)),
    transforms.ToTensor(),
    transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225])
])

# Load dữ liệu
root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "wwwroot"))
train_dataset = FlowerDataset(csv_file='data/train.csv', root_dir=root_dir, transform=train_transform)
val_dataset = FlowerDataset(csv_file='data/val.csv', root_dir=root_dir, transform=val_transform)

train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True)
val_loader = DataLoader(val_dataset, batch_size=32, shuffle=False)

# Khởi tạo mô hình
device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
model = FlowerModel(num_colors=len(COLORS), num_presentations=len(PRESENTATIONS)).to(device)
color_criterion = nn.BCELoss()
pres_criterion = nn.CrossEntropyLoss()
optimizer = optim.Adam(model.parameters(), lr=0.001)

# Huấn luyện
num_epochs = 50
best_val_loss = float('inf')
for epoch in range(num_epochs):
    model.train()
    train_loss = 0
    for images, color_labels, pres_labels in train_loader:
        images, color_labels, pres_labels = images.to(device), color_labels.to(device), pres_labels.to(device)
        optimizer.zero_grad()
        color_out, pres_out = model(images)
        color_loss = color_criterion(color_out, color_labels)
        pres_loss = pres_criterion(pres_out, pres_labels)
        loss = color_loss + pres_loss
        loss.backward()
        optimizer.step()
        train_loss += loss.item()
    
    model.eval()
    val_loss = 0
    with torch.no_grad():
        for images, color_labels, pres_labels in val_loader:
            images, color_labels, pres_labels = images.to(device), color_labels.to(device), pres_labels.to(device)
            color_out, pres_out = model(images)
            color_loss = color_criterion(color_out, color_labels)
            pres_loss = pres_criterion(pres_out, pres_labels)
            loss = color_loss + pres_loss
            val_loss += loss.item()
    
    print(f'Epoch {epoch+1}/{num_epochs}, Train Loss: {train_loss/len(train_loader):.4f}, Val Loss: {val_loss/len(val_loader):.4f}')
    
    if val_loss < best_val_loss:
        best_val_loss = val_loss
        torch.save(model.state_dict(), os.path.join(os.path.dirname(__file__), "..", "Models", "best_model.pth"))