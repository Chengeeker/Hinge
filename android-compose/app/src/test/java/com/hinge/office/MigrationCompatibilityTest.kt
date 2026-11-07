package com.hinge.office

import org.junit.Assert.*
import org.junit.Test
import org.json.JSONObject
import java.nio.ByteBuffer
import java.util.Base64

class MigrationCompatibilityTest {
    private val golden=JSONObject(requireNotNull(javaClass.getResourceAsStream("/flutter-vectors.json")).bufferedReader().use{it.readText()})
    private fun bytes(name:String)=Base64.getDecoder().decode(golden.getString(name))
    private fun rejects(block:()->Unit){try{block();fail("必须拒绝非法输入")}catch(_:IllegalArgumentException){}}
    @Test fun inputBytesMatchFlutter(){assertArrayEquals(bytes("input"),Wire.input(0x12,13,8,-12345,65535,-120,"Hinge 中文 😀"))}
    @Test fun authenticatedInputMatchesBothDirections(){
        val key=ByteArray(32){it.toByte()};val challenge=ByteArray(32){(it+32).toByte()}
        val client=RemoteCipher(key.copyOf(),challenge);val server=RemoteCipher(key.copyOf(),challenge,true)
        assertArrayEquals(bytes("client"),client.encrypt(bytes("input")))
        assertArrayEquals(bytes("server"),server.encrypt(bytes("input")))
        assertArrayEquals(bytes("input"),client.decrypt(bytes("server")))
        assertArrayEquals(bytes("input"),server.decrypt(bytes("client")))
        assertNull(client.decrypt(bytes("server")))
    }
    @Test fun tamperingDoesNotAdvanceReceiveCounter(){
        val cipher=RemoteCipher(ByteArray(32){it.toByte()},ByteArray(32){(it+32).toByte()})
        val corrupted=bytes("server").also{it[it.lastIndex]=(it.last().toInt()xor 1).toByte()}
        assertNull(cipher.decrypt(corrupted));assertArrayEquals(bytes("input"),cipher.decrypt(bytes("server")))
        assertNull(cipher.decrypt(ByteArray(16426)));assertNull(cipher.decrypt(ByteArray(40)))
    }
    @Test fun relayIdentityAndKeyMatchFlutter(){
        val secret=golden.getString("secret")
        assertEquals(golden.getString("sender"),RelayCrypto.id(secret," phone "))
        assertEquals(golden.getString("receiver"),RelayCrypto.id(secret,"desktop"))
        assertArrayEquals(bytes("transferKey"),RelayCrypto.derive(secret,golden.getString("transferId"),golden.getString("sender"),golden.getString("receiver")))
    }
    @Test fun relayPartAndMetadataMatchFlutter(){
        val id=golden.getString("transferId");val sender=golden.getString("sender");val receiver=golden.getString("receiver");val plain=bytes("plain")
        val aad=RelayCrypto.aad(id,sender,receiver,3,plain.size)
        assertArrayEquals(bytes("part"),RelayCrypto.crypt(true,bytes("transferKey"),RelayCrypto.nonce(id,3),aad,plain))
        assertArrayEquals(plain,RelayCrypto.crypt(false,bytes("transferKey"),RelayCrypto.nonce(id,3),aad,bytes("part")))
        val metadata=golden.getJSONObject("metadata")
        assertArrayEquals(RelayCrypto.decode(metadata.getString("nonce")),RelayCrypto.metadataNonce(id))
        val json=JSONObject(String(RelayCrypto.crypt(false,bytes("transferKey"),RelayCrypto.metadataNonce(id),RelayCrypto.metadataAad(id,sender,receiver),RelayCrypto.decode(metadata.getString("ciphertext"))+RelayCrypto.decode(metadata.getString("tag")))))
        assertEquals("迁移.txt",json.getString("fileName"));assertEquals(4096,json.getInt("fileSize"))
    }
    @Test fun multipartBoundaries(){
        assertEquals(1,RelayCrypto.partCount(0));assertEquals(1,RelayCrypto.partCount(RelayCrypto.PART.toLong()))
        assertEquals(2,RelayCrypto.partCount(RelayCrypto.PART+1L));assertEquals(10000,RelayCrypto.partCount(RelayCrypto.PART*10000L))
        rejects{RelayCrypto.partCount(-1)};rejects{RelayCrypto.partCount(RelayCrypto.PART*10000L+1)}
    }
    @Test fun compressedFrameMatchesFlutter(){val result=Wire.unwrap(0x82,bytes("compressed"));assertEquals(0x81,result.first);assertArrayEquals(bytes("plain"),result.second)}
    @Test fun compressedFrameRejectsBombsWrongTypesAndTrailingData(){
        val frame=bytes("compressed")
        rejects{Wire.unwrap(0x82,frame.copyOf(8))}
        rejects{Wire.unwrap(0x82,frame.copyOf().also{it[1]=1})}
        rejects{Wire.unwrap(0x82,frame.copyOf().also{ByteBuffer.wrap(it).putShort(2,0x33)})}
        rejects{Wire.unwrap(0x82,frame.copyOf().also{ByteBuffer.wrap(it).putInt(4,Int.MAX_VALUE)})}
        rejects{Wire.unwrap(0x82,frame.copyOf().also{ByteBuffer.wrap(it).putInt(4,20)})}
        rejects{Wire.unwrap(0x82,frame+byteArrayOf(0))}
    }
    @Test fun presetsPreserveSelectionAndRejectDuplicates(){
        val preset=GamePreset.desktop();val doc=PresetDocument(preset.id,listOf(preset));assertEquals(doc,PresetDocument.decode(doc.encode()))
        rejects{PresetDocument(preset.id,listOf(preset,preset)).encode()}
        rejects{GamePreset.from(preset.map()+mapOf("controls" to listOf(preset.controls[0].map(),preset.controls[0].map())))}
        rejects{GamePreset.from(preset.map()+mapOf("controls" to listOf("invalid")))}
        rejects{GamePreset.from(preset.map()+mapOf("controls" to List(65){GameControl().map()}))}
        val first=GameControl(id="a".repeat(80)+"1");val second=first.copy(id="a".repeat(80)+"2")
        rejects{GamePreset.from(preset.map()+mapOf("controls" to listOf(first.map(),second.map())))}
    }
    @Test fun controlsNormalizeUntrustedNumbers(){
        val c=GameControl.from(mapOf("id" to "a","x" to Float.NaN,"y" to 9,"size" to -1,"binding" to 65.5,"shape" to "circle","aspect" to 2))
        assertEquals(.5f,c.x);assertEquals(1f,c.y);assertEquals(.1f,c.size);assertEquals(0,c.binding);assertEquals(1f,c.aspect)
    }
    @Test fun shortScreensAndRuntimeToolbarKeepControlsReachable(){
        for((width,height)in listOf(800f to 360f,568f to 256f,320f to 150f))for(c in GamePreset.desktop().controls){
            val rect=controlRect(c,width,height);assertTrue(rect.x>=0&&rect.y>=0&&rect.x+rect.width<=width&&rect.y+rect.height<=height)
        }
        val round=controlRect(GameControl(joystick=true,aspect=3f),800f,360f);assertEquals(round.width,round.height)
        val toolbar=runtimeControlRect(GameControl(x=.5f,y=0f),800f,360f,true);assertEquals(60f,toolbar.y)
    }
    @Test fun joystickDeadzoneAndDiagonal(){assertTrue(joystickKeys(.05f,.04f,listOf(87,65,83,68)).isEmpty());assertEquals(setOf(87,68),joystickKeys(.8f,-.9f,listOf(87,65,83,68)));assertEquals(setOf(87),joystickKeys(.1f,-.9f,listOf(87,65,83,68)))}
    @Test fun oldAndCurrentHistoryKeepRecordsAndSuppression(){
        val records="[{\"id\":\"old\",\"status\":\"completed\",\"localPath\":\"/storage/file.txt\"}]"
        val legacy=readTransferHistory(records)
        val current=readTransferHistory("{\"records\":$records,\"suppressedTransferIds\":[\"active\"]}")
        assertEquals(legacy.first,current.first);assertEquals("/storage/file.txt",current.first.first()["localPath"])
        assertTrue(legacy.second.isEmpty());assertEquals(listOf("active"),current.second)
        rejects{readTransferHistory("123")}
        rejects{readTransferHistory("{}")}
    }
    @Test fun startupReconcilesOnlyStaleTransfers(){
        val rows = listOf(
            mapOf("id" to "native-active", "status" to "queued"),
            mapOf("id" to "native-stale", "status" to "queued"),
            mapOf("id" to "lan-stale", "status" to "transferring"),
            mapOf("id" to "cloud-upload", "status" to "awaitingPickup"),
        )
        val reconciled = reconcileTransferHistory(rows, setOf("native-active"))
        assertEquals(listOf("queued", "failed", "failed", "awaitingPickup"), reconciled.map { it.text("status") })
        assertEquals(rows.first(), reconciled.first())
        assertEquals(rows.last(), reconciled.last())
    }
    @Test fun touchpadKeepsSmallMotionRightHalfAndNearbyDoubleTap(){
        val events=mutableListOf<String>();val pad=TouchpadGesture({x,y->events+="move:$x,$y"},{events+="wheel:$it"},{events+="click:$it"},{events+="hold:$it"})
        pad.down(1,10f,10f,0,false);pad.move(1,11f,10f);pad.up(1,11f,10f,100)
        assertEquals(listOf("move:3,0","click:0"),events);events.clear()
        pad.down(1,12f,10f,300,false);pad.move(1,30f,10f);pad.cancel();assertEquals(listOf("hold:true","move:54,0","hold:false"),events);events.clear()
        pad.down(1,200f,10f,600,true);pad.up(1,200f,10f,1500);assertEquals(listOf("click:1"),events)
    }
    @Test fun twoFingerTapAndScrollNeverBecomeLeftClick(){
        val clicks=mutableListOf<Int>();val scroll=mutableListOf<Int>();val pad=TouchpadGesture({_,_->},{scroll+=it},{clicks+=it},{})
        pad.down(1,0f,0f,0,false);pad.down(2,40f,0f,10,false);pad.up(1,0f,0f,20);pad.up(2,40f,0f,30);assertEquals(listOf(1),clicks)
        clicks.clear();pad.down(1,0f,0f,100,false);pad.down(2,40f,0f,110,false);pad.move(1,0f,20f);pad.up(1,0f,20f,130);pad.up(2,40f,0f,140);assertTrue(clicks.isEmpty());assertEquals(listOf(-60),scroll)
    }
    @Test fun frameStatisticsUseBoundedRecentSamplesAndExpire(){val samples=FrameSamples();assertNull(samples.p95(0));for(i in 1..20)samples.add(i*10L,i*1000000L);assertEquals(19.0,samples.p95(200)!!,.001);samples.add(100,-1);assertEquals(19.0,samples.p95(200)!!,.001);assertNull(samples.p95(4000))}
}
