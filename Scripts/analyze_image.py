import cv2
import numpy as np
import sys
import json
import filetype
from sklearn.cluster import KMeans
from skimage import color

sys.stdout.reconfigure(encoding='utf-8')

def map_rgb_to_name(rgb):
    colors = {
        'đỏ': [255, 0, 0], 'đỏ đậm': [139, 0, 0], 'đỏ tươi': [255, 69, 0], 'đỏ cherry': [222, 49, 99], 'đỏ rượu': [153, 0, 0],
        'đỏ hồng': [255, 99, 71], 'hồng': [255, 192, 203], 'hồng nhạt': [255, 182, 193], 'hồng đậm': [255, 105, 180],
        'hồng phấn': [255, 204, 204], 'hồng đào': [255, 218, 185], 'hồng san hô': [255, 127, 127], 'hồng cẩm chướng': [255, 153, 204],
        'hồng dâu': [255, 105, 97], 'hồng phai': [219, 112, 147], 'hồng sen': [255, 145, 164], 'trắng': [255, 255, 255],
        'trắng kem': [255, 245, 238], 'kem': [255, 253, 208], 'trắng ngọc trai': [240, 248, 255], 'trắng sữa': [245, 245, 220],
        'vàng': [255, 255, 0], 'vàng nhạt': [255, 255, 224], 'vàng đậm': [255, 215, 0], 'vàng cam': [255, 195, 0],
        'vàng cúc': [255, 228, 181], 'vàng mù tạt': [255, 219, 88], 'vàng ánh kim': [255, 215, 0], 'vàng đồng tiền': [255, 223, 0],
        'cam': [255, 165, 0], 'cam cháy': [255, 140, 0], 'cam đào': [255, 178, 128], 'cam san hô': [255, 160, 122],
        'cam đất': [255, 127, 80], 'cam rực': [255, 117, 24], 'tím': [128, 0, 128], 'tím nhạt': [230, 230, 250],
        'tím violet': [238, 130, 238], 'tím đậm': [102, 51, 153], 'tím oải hương': [204, 153, 255], 'tím lan': [186, 85, 211],
        'tím mộng mơ': [221, 160, 221], 'tím hoàng gia': [75, 0, 130], 'tím huệ': [147, 112, 219], 'xanh dương': [0, 0, 255],
        'xanh dương nhạt': [173, 216, 230], 'xanh ngọc': [64, 224, 208], 'xanh biển': [0, 105, 148], 'xanh cobalt': [0, 71, 171],
        'xanh sapphire': [15, 82, 186], 'xanh cẩm tú cầu': [135, 206, 250], 'xanh bạc hà': [152, 255, 152], 'xanh lam': [70, 130, 180],
        'xanh lá': [0, 128, 0], 'xanh lá nhạt': [144, 238, 144], 'xanh olive': [107, 142, 35], 'xanh rêu': [47, 79, 79],
        'xanh pastel': [189, 252, 201], 'xanh đậm': [0, 100, 0], 'xanh lá mạ': [124, 252, 0], 'nâu': [165, 42, 42],
        'nâu nhạt': [210, 180, 140], 'nâu socola': [139, 69, 19], 'nâu cà phê': [111, 78, 55], 'nâu đất': [139, 69, 19],
        'xám': [128, 128, 128], 'xám nhạt': [211, 211, 211], 'đen': [0, 0, 0]
    }

    rgb = np.array(rgb, dtype=np.float32) / 255.0
    rgb = rgb.reshape(1, 1, 3)
    lab = color.rgb2lab(rgb)[0][0]

    min_distance = float('inf')
    closest_color = None

    for name, rgb_value in colors.items():
        rgb_ref = np.array(rgb_value, dtype=np.float32) / 255.0
        rgb_ref = rgb_ref.reshape(1, 1, 3)
        lab_ref = color.rgb2lab(rgb_ref)[0][0]
        distance = np.sqrt(np.sum((lab - lab_ref) ** 2))
        if distance < min_distance:
            min_distance = distance
            closest_color = name

    if min_distance > 35:
        return 'không xác định'
    
    return closest_color

def get_dominant_colors(image_path, num_colors=6, exclude_background=True):
    try:
        kind = filetype.guess(image_path)
        if kind is None or kind.mime not in ['image/jpeg', 'image/png', 'image/webp']:
            raise ValueError(f"File không phải hình ảnh hợp lệ: {image_path}")
        
        img_array = np.fromfile(image_path, dtype=np.uint8)
        img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
        if img is None:
            raise ValueError(f"Không thể đọc ảnh tại: {image_path}")
        
        # Tiền xử lý: Chuẩn hóa ánh sáng và làm mịn
        img_lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(img_lab)
        clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
        l = clahe.apply(l)
        img_lab = cv2.merge((l, a, b))
        img = cv2.cvtColor(img_lab, cv2.COLOR_LAB2BGR)
        img = cv2.GaussianBlur(img, (5, 5), 0)
        img = cv2.resize(img, (224, 224))
        img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        
        # Phân đoạn hoa bằng GrabCut
        mask = np.zeros(img.shape[:2], np.uint8)
        bgd_model = np.zeros((1, 65), np.float64)
        fgd_model = np.zeros((1, 65), np.float64)
        rect = (10, 10, img.shape[1]-10, img.shape[0]-10)
        cv2.grabCut(img, mask, rect, bgd_model, fgd_model, 5, cv2.GC_INIT_WITH_RECT)
        mask2 = np.where((mask == 2) | (mask == 0), 0, 1).astype('uint8')
        
        # Chuyển sang HSV để tạo mặt nạ bổ sung
        img_hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        lower_white = np.array([0, 0, 210])
        upper_white = np.array([180, 15, 255])
        lower_black = np.array([0, 0, 0])
        upper_black = np.array([180, 255, 30])
        lower_gray = np.array([0, 0, 50])
        upper_gray = np.array([180, 25, 200])
        mask_white = cv2.inRange(img_hsv, lower_white, upper_white)
        mask_black = cv2.inRange(img_hsv, lower_black, upper_black)
        mask_gray = cv2.inRange(img_hsv, lower_gray, upper_gray)
        mask_hsv = cv2.bitwise_not(mask_white + mask_black + mask_gray)
        
        # Ưu tiên vùng có độ bão hòa cao
        saturation = img_hsv[:, :, 1]
        sat_mask = cv2.inRange(saturation, 60, 255)
        final_mask = cv2.bitwise_and(mask2, mask_hsv, mask=sat_mask)
        
        # Áp dụng mặt nạ
        masked_img = cv2.bitwise_and(img_rgb, img_rgb, mask=final_mask)
        pixels = masked_img.reshape(-1, 3)
        valid_pixels = pixels[np.any(pixels != [0, 0, 0], axis=1)]
        
        if len(valid_pixels) < 100:
            raise ValueError("Không đủ pixel hợp lệ sau khi phân đoạn")
        
        if len(valid_pixels) > 10000:
            indices = np.random.choice(len(valid_pixels), 10000, replace=False)
            valid_pixels = valid_pixels[indices]
        
        kmeans = KMeans(n_clusters=num_colors, random_state=42, n_init=20)
        kmeans.fit(valid_pixels)
        
        colors = kmeans.cluster_centers_.astype(int)
        labels = kmeans.labels_
        color_counts = np.bincount(labels)
        color_proportions = color_counts / len(labels)
        
        sorted_indices = np.argsort(color_proportions)[::-1]
        final_colors = []
        for i in sorted_indices:
            color_name = map_rgb_to_name(colors[i])
            if exclude_background and color_name in ['trắng', 'đen', 'xám', 'xám nhạt', 'trắng kem', 'trắng sữa'] and color_proportions[i] > 0.15:
                continue
            if color_name != 'không xác định':
                final_colors.append(color_name)
        
        if not final_colors and colors.any():
            final_colors.append(map_rgb_to_name(colors[sorted_indices[0]]))
        
        return list(set(final_colors))[:4]
    except Exception as e:
        raise Exception(f"Lỗi khi phân tích màu sắc: {str(e)}")

def classify_presentation(image_path):
    try:
        kind = filetype.guess(image_path)
        if kind is None or kind.mime not in ['image/jpeg', 'image/png', 'image/webp']:
            raise ValueError(f"File không phải hình ảnh hợp lệ: {image_path}")
        
        img_array = np.fromfile(image_path, dtype=np.uint8)
        img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
        if img is None:
            raise ValueError(f"Không thể đọc ảnh tại: {image_path}")
        
        img = cv2.resize(img, (224, 224))
        height, width = img.shape[:2]
        aspect_ratio = width / height
        
        # Chuyển sang RGB để phân tích màu nền
        img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        
        # Tính mật độ biên
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 100, 200)
        edge_density = np.sum(edges) / (height * width)
        
        # Tính số lượng đường viền
        _, thresh = cv2.threshold(gray, 240, 255, cv2.THRESH_BINARY_INV)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        contour_count = len(contours)
        
        # Tính tỷ lệ vùng sáng
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        brightness = hsv[:, :, 2]
        bright_ratio = np.sum(brightness > 210) / (height * width)
        
        # Tính độ đối xứng
        left_half = gray[:, :width//2]
        right_half = gray[:, width//2:]
        right_half_flipped = cv2.flip(right_half, 1)
        symmetry_score = np.mean(np.abs(left_half - right_half_flipped)) / 255.0
        symmetry_score = 1.0 - symmetry_score
        
        # Phân tích màu nền
        border_pixels = np.concatenate([img_rgb[0, :], img_rgb[-1, :], img_rgb[:, 0], img_rgb[:, -1]])
        border_colors = [map_rgb_to_name(pixel) for pixel in border_pixels]
        border_color_counts = {}
        for c in border_colors:
            if c != 'không xác định':
                border_color_counts[c] = border_color_counts.get(c, 0) + 1
        dominant_border_color = max(border_color_counts, key=border_color_counts.get, default='không xác định') if border_color_counts else 'không xác định'
        is_uniform_background = border_color_counts.get(dominant_border_color, 0) / len(border_pixels) > 0.7
        
        # Quy tắc phân loại
        if aspect_ratio < 0.7 and edge_density < 0.08:
            return "Bó hoa"
        elif aspect_ratio < 0.7 and edge_density >= 0.08:
            return "Hoa bó cổ điển"
        elif 0.9 < aspect_ratio < 1.1 and contour_count > 25 and symmetry_score < 0.8:
            return "Giỏ hoa"
        elif 0.9 < aspect_ratio < 1.1 and contour_count <= 25 and symmetry_score >= 0.8:
            return "Giỏ hoa hiện đại"
        elif aspect_ratio > 1.5 and edge_density < 0.07 and is_uniform_background:
            return "Hộp hoa"
        elif aspect_ratio > 1.5 and edge_density >= 0.07:
            return "Hộp hoa nghệ thuật"
        elif edge_density > 0.18 and contour_count > 35:
            return "Lẵng hoa"
        elif edge_density > 0.18 and contour_count <= 35:
            return "Lẵng hoa mini"
        elif bright_ratio > 0.35 and contour_count < 8:
            return "Bình hoa"
        else:
            return "Hoa để bàn"
    except Exception as e:
        raise Exception(f"Lỗi khi phân loại kiểu trình bày: {str(e)}")

def main():
    try:
        if len(sys.argv) < 2:
            raise ValueError("Vui lòng cung cấp đường dẫn ảnh")
        
        image_path = sys.argv[1]
        with open('analysis_log.txt', 'a', encoding='utf-8') as log_file:
            log_file.write(f"Processing image: {image_path}\n")
        
        colors = get_dominant_colors(image_path, num_colors=6)
        presentation = classify_presentation(image_path)
        
        result = {
            'success': True,
            'colors': colors,
            'presentation': presentation
        }
        
        with open('analysis_log.txt', 'a', encoding='utf-8') as log_file:
            log_file.write(f"Result: {json.dumps(result, ensure_ascii=False)}\n")
    except Exception as e:
        result = {
            'success': False,
            'message': str(e)
        }
        with open('analysis_log.txt', 'a', encoding='utf-8') as log_file:
            log_file.write(f"Error: {str(e)}\n")
    
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()