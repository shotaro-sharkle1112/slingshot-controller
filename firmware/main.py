# combined_bend_mpu_serial.py
# Pico W / MicroPython
# USBシリアルで Unity にセンサ値を送信する
#
# 出力例:
# {"t_ms":12345,"bend":30120,"accel":{"x":0.01,"y":0.02,"z":1.01},"gyro":{"x":0.1,"y":0.2,"z":0.3}}

import time
import sys

try:
    import ujson as json
except ImportError:
    import json

from machine import ADC, Pin, I2C
from imu import MPU6050  # imu.py が Pico 側に必要

led = Pin("LED", Pin.OUT)

# =========================
# 送信周期
# =========================
# 20ms = 50Hz
SEND_INTERVAL_MS = 20

# =========================
# 曲げセンサ / ADC
# =========================
# ADC0 = GPIO26
bend_sensor = ADC(Pin(26))

latest_raw = 0
latest_ema = 0.0
EMA_ALPHA = 0.3


def update_bend_raw():
    """曲げセンサのADC raw値を更新する。read_u16() は 0〜65535 を返す。"""
    global latest_raw, latest_ema

    latest_raw = bend_sensor.read_u16()
    latest_ema = EMA_ALPHA * latest_raw + (1 - EMA_ALPHA) * latest_ema

    return int(latest_raw)


# =========================
# I2C & MPU6050
# =========================
# 配線例: GP0→SDA, GP1→SCL, 3V3→VCC, GND→GND, AD0→GND(=0x68)
i2c = I2C(0, sda=Pin(0), scl=Pin(1), freq=400000)
mpu = MPU6050(i2c)

# 元コードと同じ設定
mpu.accel_range = 1
mpu.gyro_range = 2
mpu.filter_range = 2  # 41Hz


def read_mpu():
    """MPU6050の加速度・角速度をdictで返す。"""
    ax, ay, az = mpu.accel.xyz  # g
    gx, gy, gz = mpu.gyro.xyz   # deg/s

    return {
        "accel": {
            "x": ax,
            "y": ay,
            "z": az
        },
        "gyro": {
            "x": gx,
            "y": gy,
            "z": gz
        }
    }


def make_payload():
    bend = update_bend_raw()
    mpu_data = read_mpu()

    return {
        "t_ms": time.ticks_ms(),
        "bend": bend,
        "accel": mpu_data["accel"],
        "gyro": mpu_data["gyro"]
    }


def send_json(payload):
    # Unity 側で ReadLine() しやすいように、必ず末尾に \n を付ける
    line = json.dumps(payload)
    sys.stdout.write(line + "\n")


# =========================
# メインループ
# =========================
counter = 0

while True:
    try:
        payload = make_payload()
        send_json(payload)

        # 動作確認用LED
        counter += 1
        if counter % 25 == 0:
            led.toggle()

    except Exception as e:
        # エラーもJSON形式で送る
        error_payload = {
            "error": str(e)
        }
        send_json(error_payload)

    time.sleep_ms(SEND_INTERVAL_MS)