PIC & PRezr
===========

PIC is an image quantizer and ditherer for Pebble Time watchface images. It was used to create the images for Pebble DOOM 2: https://github.com/PJayB/PebbleDoom2.

Dithering patterns supported:

* False Floyd Steinberg
* Floyd Steinberg
* Fan
* Jarvis, Judice, Ninke
* Atkinson
* Two Row Sierra
* Sierra
* Sierra Lite

Prezr is an image packaging application for Pebble images. It converts pre-quantized images into the smallest possible native Pebble format and generates C utility code to load them.

Image output support:

* 1-bit black and white
* 2-bit palettized 
* 4-bit palettized
* 8-bit (2-bits per channel with alpha)

