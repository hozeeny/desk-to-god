"""Mechanical polygon separation of the approved three portraits. No repainting.
Each atlas has eleven overlapping source-pixel parts and one unchanged reference.
The anchors retain source-space geometry and convert it to the 220x240 stage.
"""
from pathlib import Path
import hashlib, json
from PIL import Image, ImageDraw, ImageChops

ROOT=Path(__file__).resolve().parents[2]
ASSETS=ROOT/'源代码'/'ArtDemoAssets'
QA=ROOT/'美术Demo'/'qa'/'hero-rig'
QA.mkdir(parents=True,exist_ok=True)
POLYGONS=[
 [(215,0),(710,0),(720,419),(573,438),(471,424),(397,389),(340,347),(280,316),(236,268),(213,179)],
 [(326,374),(410,399),(492,402),(567,457),(567,520),(536,586),(525,640),(535,686),(563,732),(582,774),(452,810),(299,784),(261,760),(292,662),(320,588),(291,499),(291,450)],
 [(283,422),(334,445),(329,538),(287,610),(277,672),(220,688),(153,649),(155,576),(200,492)],
 [(170,622),(254,636),(246,703),(239,738),(258,776),(261,797),(243,830),(199,834),(155,801),(160,748),(166,693)],
 [(535,444),(579,470),(596,552),(632,608),(641,674),(568,708),(539,654),(531,575)],
 [(563,650),(644,649),(665,713),(701,777),(692,844),(615,850),(592,815),(582,730)],
 [(308,750),(448,753),(441,836),(404,936),(355,972),(245,969),(231,901),(268,832)],
 [(253,939),(363,942),(346,1015),(368,1105),(365,1148),(231,1148),(226,1097),(252,1014)],
 [(436,766),(560,774),(581,850),(611,924),(578,974),(452,975),(412,901)],
 [(450,943),(579,942),(584,1009),(674,1069),(710,1114),(688,1137),(503,1131),(457,1090),(451,1010)],
 [(100,130),(251,145),(260,190),(270,230),(276,250),(275,330),(281,365),(294,382),(281,402),(258,413),(235,438),(218,458),(208,487),(164,511),(119,481),(80,400)],
]
NAMES=['head','torso','near-upper-arm','near-forearm','far-upper-arm','far-forearm','near-thigh','near-shin-boot','far-thigh','far-shin-boot','ponytail','reference']
JOINTS={'neck':[423,405], 'nearShoulder':[286,467], 'nearElbow':[213,641], 'nearHand':[216,797],
        'farShoulder':[552,477], 'farElbow':[609,671], 'farHand':[649,799],
        'nearHip':[368,793], 'nearKnee':[309,950], 'nearAnkle':[300,1068], 'nearSole':[300,1137],
        'farHip':[482,798], 'farKnee':[526,948], 'farAnkle':[553,1053], 'farSole':[553,1119], 'tailRoot':[241,182]}
SCALE=.151
OFFSET=[46,14]
def point(p):return [round(OFFSET[0]+p[0]*SCALE,5),round(OFFSET[1]+p[1]*SCALE,5)]
records=[]
for stage in ['mortal','qishi','zhuji']:
 src=ASSETS/f'hero-{stage}.png'
 image=Image.open(src).convert('RGBA'); assert image.size==(768,1152)
 parts=[]; atlas=Image.new('RGBA',(1024,768)); rects=[]; metadata=[]
 polys=[list(p) for p in POLYGONS]
 if stage=='zhuji':
  # Preserve the real longer overskirt; legs articulate beneath it.
  polys[1]=[(326,374),(413,399),(492,402),(567,457),(580,550),(557,623),(575,714),(632,889),(573,870),(477,872),(407,842),(295,903),(192,896),(261,769),(292,662),(320,588),(291,499),(291,450)]
 for index in range(12):
  if index==11: masked=image.copy()
  else:
   mask=Image.new('L',image.size); ImageDraw.Draw(mask).polygon(polys[index],fill=255)
   masked=image.copy();masked.putalpha(ImageChops.multiply(image.getchannel('A'),mask))
  bounds=masked.getbbox();assert bounds
  # Two source-pixel margins are authored in every rect, then 8 atlas pixels.
  x0,y0,x1,y1=bounds;x0=max(0,x0-2);y0=max(0,y0-2);x1=min(image.width,x1+2);y1=min(image.height,y1+2)
  cut=masked.crop((x0,y0,x1,y1));factor=min(240/cut.width,240/cut.height)
  size=(max(1,round(cut.width*factor)),max(1,round(cut.height*factor)))
  cut=cut.resize(size,Image.Resampling.LANCZOS)
  cellx=index%4*256;celly=index//4*256;px=cellx+(256-size[0])//2;py=celly+(256-size[1])//2
  atlas.alpha_composite(cut,(px,py));rects.append([px,py,*size])
  metadata.append({'name':NAMES[index],'bounds':[round(OFFSET[0]+x0*SCALE,5),round(OFFSET[1]+y0*SCALE,5),round((x1-x0)*SCALE,5),round((y1-y0)*SCALE,5)],'sourceBounds':[x0,y0,x1-x0,y1-y0]})
  parts.append(cut)
 out=ASSETS/f'hero-rig-{stage}.png';atlas.save(out)
 (ASSETS/f'hero-rig-{stage}.rects').write_text('# 4 columns x 3 rows; eleven parts and unchanged source reference\n'+'\n'.join(','.join(map(str,r)) for r in rects)+'\n',encoding='utf-8')
 data={'version':1,'source':src.name,'sourceSha256':hashlib.sha256(src.read_bytes()).hexdigest(),'canvas':[220,240],'parts':metadata,'joints':{key:point(value) for key,value in JOINTS.items()},'sourceScale':SCALE,'sourceOffset':OFFSET}
 (ASSETS/f'hero-rig-{stage}.anchors').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 # Verify alpha edge safety and actual rectangular-pair ownership.
 for i,(x,y,w,h) in enumerate(rects):
  assert x//256==i%4 and y//256==i//4 and (x+w-1)//256==i%4 and (y+h-1)//256==i//4
  assert x%256>=8 and y%256>=8 and 256-x%256-w>=8 and 256-y%256-h>=8
 records.append({'stage':stage,'sourceSha256':data['sourceSha256'],'atlasSha256':hashlib.sha256(out.read_bytes()).hexdigest(),'parts':12,'independentBodyParts':11,'cellSize':256,'grid':[4,3],'outerCellSafetyPixels':8,'operation':'polygon masks + proportional resampling; no repaint, recolour or synthesis','warning':'Overlap pixels are source samples, not newly invented hidden surfaces. Extreme poses remain out of scope.'})
 # Labelled sheet is QA only, runtime atlas remains text-free.
 proof=Image.new('RGB',atlas.size,(237,233,221));proof.paste(atlas,mask=atlas.getchannel('A'));draw=ImageDraw.Draw(proof)
 for i,n in enumerate(NAMES):draw.text((i%4*256+8,i//4*256+6),n,fill=(40,45,48))
 proof.save(QA/f'{stage}-parts.png')
(QA/'asset-verification.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('3 atlases, 36 rects and 33 independent body parts verified; immutable portrait SHA-256 recorded.')
